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
        if (!_deviceEnumerator.Cameras.TryGetValue(camera, out var mappedAddress))
        {
            // Stale list (e.g. camera plugged in after launch). Re-enumerate once and retry.
            _deviceEnumerator.Cameras = _deviceEnumerator.UpdateCameras();
            _deviceEnumerator.Cameras.TryGetValue(camera, out mappedAddress);
        }
        if (mappedAddress != null)
            camera = mappedAddress;

        return Task.Run<SingleCameraSource?>(() => StartWithFallback(address, camera, providerName));
    }

    // Tries each compatible backend in preference order until one delivers a frame. A specific
    // providerName is tried first; the rest stay as fallbacks. This lets a device drop from e.g. the
    // OpenCV/GStreamer "Normal Camera" backend (which needs an unbundled v4l2src plugin) to the
    // dependency-free "V4L2 Camera" one instead of failing outright.
    private SingleCameraSource? StartWithFallback(string address, string camera, string providerName)
    {
        // The Vive Facial Tracker backend fires a native USB tracker-enable (enableViveFacialTracker)
        // *before* it ever opens the camera. On a name that isn't a real, present tracker — e.g. a
        // stale saved camera that has since been unplugged — that native call wedges a thread Windows
        // can't reap, so the process lingers as a ghost after the window closes. Only offer VFT for a
        // device the current enumeration actually knows about. Other backends open lazily and fail
        // gracefully, so they don't need this guard. (Linux's VFT only matches /dev/video* paths, which
        // don't exist when unplugged, so present setups there are unaffected.)
        var deviceIsPresent = IsEnumeratedDevice(address) || IsEnumeratedDevice(camera);
        var candidates = _platformConnector.GetCaptureFactories()
            .Where(factory => factory.CanConnect(camera))
            .Where(factory => deviceIsPresent || !IsViveFacialTracker(factory))
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

            // Create() can throw before any frame is attempted — e.g. a backend whose native deps are
            // missing runs a failing static ctor (the PSVR2 module throws TypeInitializationException
            // from Create()). Treat any such failure as "this backend can't open the device" and fall
            // through to the next candidate instead of letting it abort the whole fallback chain.
            SingleCameraSource source;
            try
            {
                source = new SingleCameraSource(_loggerFactory.CreateLogger<SingleCameraSource>(), factory.Create(camera), camera);
            }
            catch (Exception e)
            {
                _logger.LogWarning("{} threw while creating a capture for {}: {}; trying the next backend", providerLabel, address, e.Message);
                continue;
            }

            try
            {
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
            catch (Exception e)
            {
                _logger.LogWarning("{} failed while starting {}: {}; trying the next backend", providerLabel, address, e.Message);
                source.Dispose();
            }
        }

        _logger.LogError("No data was received from {} on any backend, closing... Maybe the camera is opened somewhere else?", address);
        return null;
    }

    // A device is "present" when the current enumeration knows it — either as a friendly-name key or
    // as a resolved value (a camera index like "0", a "/dev/videoN" path, or a COM port). Used to keep
    // the wedge-prone VFT backend off stale/absent device names.
    private bool IsEnumeratedDevice(string address)
    {
        var cameras = _deviceEnumerator.Cameras;
        return cameras != null && (cameras.ContainsKey(address) || cameras.Values.Contains(address));
    }

    // Matches VFTCaptureFactory.GetProviderName().
    private static bool IsViveFacialTracker(ICaptureFactory factory) =>
        string.Equals(factory.GetProviderName(), "Vive Facial Tracker", StringComparison.Ordinal);
}
