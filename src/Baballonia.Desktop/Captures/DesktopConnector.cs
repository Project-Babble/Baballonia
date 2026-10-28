using Baballonia.SDK;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;

namespace Baballonia.Desktop.Captures;

/// <summary>
/// Base class for camera capture and frame processing
/// Use OpenCV's IP capture class here!
/// </summary>
public class DesktopConnector : IPlatformConnector
{
    private List<ICaptureFactory> _captureFactories;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DesktopConnector> _logger;

    public DesktopConnector(ILogger<DesktopConnector> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        // Load all modules
        var dlls = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Modules"), "*.dll");
        _logger.LogDebug("Found {DllCount} DLL files in application directory: {DllFiles}", dlls.Length,
            string.Join(", ", dlls.Select(Path.GetFileName)));
        _captureFactories = LoadAssembliesFromPath(dlls);

        // Directory.GetFiles order is arbitrary, so the default backend picked for a /dev/video*
        // device was effectively random. On Linux prefer "V4L2 Camera" (LibV4L2): it reads V4L2
        // directly and needs no GStreamer plugins, unlike the OpenCV/GStreamer "Normal Camera"
        // backend whose v4l2src dependency isn't bundled. OrderBy is stable, so the rest keep order.
        if (OperatingSystem.IsLinux())
            _captureFactories = _captureFactories
                .OrderBy(f => f.GetProviderName() == "V4L2 Camera" ? 0 : 1)
                .ToList();

        _logger.LogDebug("Loaded {CaptureCount} capture types from assemblies", _captureFactories.Count);
    }

    private List<ICaptureFactory> LoadAssembliesFromPath(string[] paths)
    {
        var returnList = new List<ICaptureFactory>();

        foreach (var dll in paths)
        {
            try
            {
                var alc = new AssemblyLoadContext(dll, true);
                var loaded = alc.LoadFromAssemblyPath(dll);

                _logger.LogDebug("Scanning assembly '{AssemblyName}' for capture types", loaded.FullName);
                foreach (var type in loaded.GetExportedTypes())
                {
                    _logger.LogDebug("Checking type '{TypeName}' for Capture compatibility", type.FullName);
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(ICaptureFactory).IsAssignableFrom(type)) continue;

                    var factory = (ICaptureFactory)ActivatorUtilities.CreateInstance(_serviceProvider, type);

                    returnList.Add(factory);
                    _logger.LogDebug("Successfully loaded capture type '{CaptureTypeName}'", type.Name);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning("Assembly '{DllPath}' not able to be loaded. Skipping. Error: {ErrorMessage}", dll,
                    e.Message);
            }
        }

        return returnList;
    }

    public ICaptureFactory[] GetCaptureFactories()
    {
        return _captureFactories.ToArray();
    }
}
