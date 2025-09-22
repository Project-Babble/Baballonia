using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Factories;
using Baballonia.Services.Inference.VideoSources;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services.Inference;

public class SingleCameraSourceFactory
{
    private readonly ILogger<SingleCameraSourceFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDeviceEnumerator _deviceEnumerator;

    public SingleCameraSourceFactory(ILogger<SingleCameraSourceFactory> logger, ILoggerFactory loggerFactory, IDeviceEnumerator deviceEnumerator)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _deviceEnumerator = deviceEnumerator;
    }

    public SingleCameraSource? Create(string address)
    {
        var platform =
            new PlatformConnectorFactory().Create(_loggerFactory.CreateLogger<PlatformConnectorFactory>(), address);
        if (platform != null)
            return new SingleCameraSource(_loggerFactory.CreateLogger<SingleCameraSource>(), platform, address);

        return null;
    }

    public Task<SingleCameraSource?> CreateStart(string address)
    {
        var camera = address;
        if (string.IsNullOrEmpty(camera)) return null;

        _deviceEnumerator.Cameras ??= _deviceEnumerator.UpdateCameras();

        if (_deviceEnumerator.Cameras.TryGetValue(camera, out var mappedAddress))
        {
            camera = mappedAddress;
        }

        return Task.Run<SingleCameraSource?>(() =>
        {
            var cameraSource = Create(camera);
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

            _logger.LogError("No data was received from {}, closing...", address);
            cameraSource.Dispose();
            return null;
        });
    }
}
