using Baballonia.Contracts;
using Baballonia.SDK;
using Baballonia.Services.Inference.Platforms;
using Baballonia.Services.Inference.VideoSources;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Services.Inference;

public class SingleCameraSourceFactory
{
    private readonly ILogger<SingleCameraSourceFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDeviceEnumerator _deviceEnumerator;
    private readonly IPlatformConnector _platformConnector;

    public SingleCameraSourceFactory(ILogger<SingleCameraSourceFactory> logger, ILoggerFactory loggerFactory, IDeviceEnumerator deviceEnumerator, IPlatformConnector platformConnector)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _deviceEnumerator = deviceEnumerator;
        _platformConnector = platformConnector;
    }

    public SingleCameraSource? Create(string address, string providerName)
    {
        ICaptureFactory? provider;
        if (!string.IsNullOrEmpty(providerName))
        {
            provider = _platformConnector.GetCaptureFactories()
                .FirstOrDefault(factory => factory.GetProviderName() == providerName && factory.CanConnect(address));
            if(provider == null)
                throw new ArgumentNullException($"No provider \"{provider}\" is not compatible with \"{address}\"");

        }
        else
        {
            provider = _platformConnector.GetCaptureFactories().First(factory => factory.CanConnect(address));
            if(provider == null)
                throw new ArgumentNullException($"No suitable provider for {address} found");
        }

        var capture = provider.Create(address);

        return new SingleCameraSource(_loggerFactory.CreateLogger<SingleCameraSource>(), capture, address);
    }

    public Task<SingleCameraSource?> CreateStart(string address) => CreateStart(address, "");

    public Task<SingleCameraSource?> CreateStart(string address, string providerName)
    {
        var camera = address;
        _deviceEnumerator.Cameras ??= _deviceEnumerator.UpdateCameras();
        if (_deviceEnumerator.Cameras.TryGetValue(camera, out var mappedAddress))
            camera = mappedAddress;

        return Task.Run<SingleCameraSource?>(() => StartWithFallback(address, camera, providerName));
    }

    // Tries each compatible backend in preference order until one delivers a frame. A specific
    // providerName is tried first; the rest stay as fallbacks. This lets a device drop from e.g. the
    // OpenCV/GStreamer "Normal Camera" backend (which needs an unbundled v4l2src plugin) to the
    // dependency-free "V4L2 Camera" one instead of failing outright.
    private SingleCameraSource? StartWithFallback(string address, string camera, string providerName)
    {
        var candidates = _platformConnector.GetCaptureFactories()
            .Where(factory => factory.CanConnect(camera))
            .ToList();

        if (!string.IsNullOrEmpty(providerName))
            candidates = candidates.OrderBy(factory => factory.GetProviderName() == providerName ? 0 : 1).ToList();

        if (candidates.Count == 0)
        {
            _logger.LogError("No capture backend can open {}", address);
            return null;
        }

        foreach (var factory in candidates)
        {
            var providerLabel = factory.GetProviderName();
            var source = new SingleCameraSource(_loggerFactory.CreateLogger<SingleCameraSource>(), factory.Create(camera), camera);

            if (!source.Start())
            {
                _logger.LogWarning("{} could not open {}; trying the next backend", providerLabel, address);
                source.Dispose();
                continue;
            }

            var waitHandles = source.GetFrameWaitHandles();
            var sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(13);
            while (sw.Elapsed < timeout)
            {
                // Block until a frame is signalled rather than busy-polling.
                WaitHandle.WaitAny(waitHandles, TimeSpan.FromMilliseconds(250));
                using var frame = source.GetFrame();
                if (frame != null)
                {
                    _logger.LogInformation("Opened {} with {}", address, providerLabel);
                    return source;
                }
            }

            _logger.LogWarning("No data from {} via {}; trying the next backend", address, providerLabel);
            source.Dispose();
        }

        _logger.LogError("No data was received from {} on any backend, closing... Maybe the camera is opened somewhere else?", address);
        return null;
    }
}
