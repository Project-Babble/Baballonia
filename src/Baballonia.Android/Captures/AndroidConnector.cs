using Baballonia.SDK;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Baballonia.Android.Captures;

public class AndroidConnector : IPlatformConnector
{
    private readonly List<ICaptureFactory> _captureFactories;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AndroidConnector> _logger;

    public AndroidConnector(IServiceProvider serviceProvider, ILogger<AndroidConnector> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _captureFactories = LoadFactories();
        _logger.LogDebug("Loaded {CaptureCount} capture types", _captureFactories.Count);
    }

    private List<ICaptureFactory> LoadFactories()
    {
        // Hardcoding this, I don't want to mess around with DLL loading on Android
        var types = new[]
        {
            typeof(IpCameraCaptureFactory),
            typeof(AndroidCamera2CaptureFactory)
        };

        var returnList = new List<ICaptureFactory>();

        foreach (var type in types)
        {
            try
            {
                _logger.LogDebug("Loading capture type '{CaptureTypeName}'", type.Name);
                var factory = (ICaptureFactory)ActivatorUtilities.CreateInstance(_serviceProvider, type);
                returnList.Add(factory);
                _logger.LogDebug("Successfully loaded capture type '{CaptureTypeName}'", type.Name);
            }
            catch (Exception e)
            {
                _logger.LogWarning("Capture type '{CaptureTypeName}' could not be loaded. Skipping. Error: {ErrorMessage}",
                    type.Name, e.Message);
            }
        }

        return returnList;
    }

    public ICaptureFactory[] GetCaptureFactories()
    {
        return _captureFactories.ToArray();
    }
}
