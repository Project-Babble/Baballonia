using System;
using System.Collections.Generic;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Captures;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services.Inference.Platforms;

/// <summary>
/// Base class for camera capture and frame processing
/// Use OpenCV's IP capture class here!
/// </summary>
public class DesktopConnector : PlatformConnector
{
    private static readonly HashSet<string> SerialConnections
        = new(StringComparer.OrdinalIgnoreCase) { "com", "/dev/ttyacm", "/dev/tty.usb", "/dev/cu.usb" };

    private static readonly HashSet<string> VftConnections
        = new(StringComparer.OrdinalIgnoreCase) { "/dev/video" };

    protected override Type DefaultCapture => typeof(OpenCvCapture);

    public DesktopConnector(string url, ILogger logger, ILocalSettingsService settingsService) : base(url, logger, settingsService)
    {
        Captures = new()
        {
            { (SerialConnections, areSuffixes: false), typeof(SerialCameraCapture) },
        };

        if (OperatingSystem.IsLinux())
        {
            Captures.Add((VftConnections, areSuffixes: false), typeof(VftCapture));
        }
    }
}
