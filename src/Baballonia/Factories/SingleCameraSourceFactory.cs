using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Factories;
using Baballonia.SDK;
using Baballonia.Services.Inference.Platforms;
using Baballonia.Services.Inference.VideoSources;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;

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

    public Task<SingleCameraSource?> CreateStart(string address)
    {
        var camera = address;
        _deviceEnumerator.Cameras ??= _deviceEnumerator.UpdateCameras();
        if (_deviceEnumerator.Cameras.TryGetValue(camera, out var mappedAddress))
        {
            camera = mappedAddress;
        }

        var provider = _platformConnector.GetCaptureFactories().FirstOrDefault(factory => factory.CanConnect(camera));
        if (provider == null)
            throw new ArgumentNullException($"No provider for {address} not found");

        return CreateStart(address, provider.GetProviderName());
    }

    public Task<SingleCameraSource?> CreateStart(string address, string providerName)
    {
        var camera = address;

        _deviceEnumerator.Cameras ??= _deviceEnumerator.UpdateCameras();
        if (_deviceEnumerator.Cameras.TryGetValue(camera, out var mappedAddress))
        {
            camera = mappedAddress;
        }

        return Task.Run<SingleCameraSource?>(() =>
        {
            var cameraSource = Create(camera, providerName);
            if (cameraSource == null)
                return null;

            if (!cameraSource.Start())
            {
                _logger.LogError("Could not initialize {}", address);
                return null;
            }

            Stopwatch sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(13);
            while (sw.Elapsed < timeout)
            {
                var testFrame = cameraSource.GetFrame();
                if (testFrame != null)
                    return cameraSource;
            }

            _logger.LogError("No data was received from {}, with {}, closing... Maybe the camera is opened somewhere else?", address, providerName);
            cameraSource.Dispose();
            return null;
        });
    }
}
