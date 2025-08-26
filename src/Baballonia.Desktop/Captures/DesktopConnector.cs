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
    public DesktopConnector(string source, ILogger logger, ILocalSettingsService settingsService) : base(source, logger, settingsService)
    {
        Captures = new Dictionary<Capture, Type>();

        // Load all modules
        var dlls = Directory.GetFiles(AppContext.BaseDirectory, "*.dll");
        Captures = LoadAssembliesFromPath(dlls);
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

                foreach (var type in loaded.GetExportedTypes())
                {
                    if (!typeof(Capture).IsAssignableFrom(type) || type.IsAbstract) continue;

                    // Check if the type has a constructor that takes a string parameter (for source)
                    var constructor = type.GetConstructor([typeof(string)]);
                    if (constructor == null) continue;

                    // Create a temporary instance to access the Connections property
                    var tempInstance = (Capture)Activator.CreateInstance(type, "temp")!;
                    returnList.Add(tempInstance, type);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                //_logger.LogWarning("{error} Assembly not able to be loaded. Skipping.", e.Message);
            }
        }

        return returnList;
    }

}
