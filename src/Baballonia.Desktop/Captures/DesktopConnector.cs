using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Threading;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.Desktop.Captures;

/// <summary>
/// Base class for camera capture and frame processing
/// Use OpenCV's IP capture class here!
/// </summary>
public class DesktopConnector : PlatformConnector, IPlatformConnector
{
    private static bool _addedCaptures = false;
    public DesktopConnector(string source, ILogger logger) : base(source, logger)
    {
        // If we've already scanned for DLL's, just return the original result. Reflection is slow!
        if (_addedCaptures) return;
        // Load all modules
        var dlls = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Modules"), "*.dll");
        Logger.LogDebug("Found {DllCount} DLL files in application directory: {DllFiles}", dlls.Length, string.Join(", ", dlls.Select(Path.GetFileName)));
        var results = LoadAssembliesFromPath(dlls);
        foreach (var pair in results) Captures.Add(pair.Key, pair.Value);
        Logger.LogDebug("Loaded {CaptureCount} capture types from assemblies", Captures.Count);
        _addedCaptures = true;
    }

    private Dictionary<Capture, Type> LoadAssembliesFromPath(string[] paths)
    {
        var returnList = new Dictionary<Capture, Type>();

        foreach (var dll in paths)
        {
            try
            {
                var alc = new AssemblyLoadContext(dll, true);
                var loaded = alc.LoadFromAssemblyPath(dll);

                Logger.LogDebug("Scanning assembly '{AssemblyName}' for capture types", loaded.FullName);
                foreach (var type in loaded.GetExportedTypes())
                {
                    Logger.LogDebug("Checking type '{TypeName}' for Capture compatibility", type.FullName);
                    if (!typeof(Capture).IsAssignableFrom(type) || type.IsAbstract) continue;

                    // Check if the type has a constructor that takes a string parameter (for source) and a logger
                    // Adding this second parameter makes reflection take longer...
                    var constructor = type.GetConstructor([typeof(string), typeof(ILogger)] );
                    if (constructor == null) continue;

                    // Create a temporary instance to access the Connections property
                    var tempInstance = (Capture)Activator.CreateInstance(type, "temp", null!)!;
                    returnList.Add(tempInstance, type);
                    Logger.LogDebug("Successfully loaded capture type '{CaptureTypeName}'", type.Name);
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning("Assembly '{DllPath}' not able to be loaded. Skipping. Error: {ErrorMessage}", dll, e.Message);
            }
        }

        return returnList;
    }

}
