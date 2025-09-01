using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Baballonia.Contracts;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;

namespace Baballonia.Android.Captures;

/// <summary>
/// Special class for iOS, Android and UWP platforms where VideoCapture is not fully implemented
/// Support for MJPEG video streams only presently!
/// </summary>
public class AndroidConnector : PlatformConnector, IPlatformConnector
{
    private static readonly HashSet<Regex> IpConnectionsPrefixes
        = new() { new Regex(@"^https?://", RegexOptions.IgnoreCase) };

    private static readonly HashSet<Regex> IpConnectionsSuffixes
        = new() { new Regex(@"\.local/?$", RegexOptions.IgnoreCase) };

    private static readonly HashSet<Regex> AndroidCamera2CapturePrefixes
        = new() { new Regex(@"^\d+$") };

    public AndroidConnector(string url, ILogger logger, ILocalSettingsService settingsService) : base(url, logger)
    {
        Captures = new()
        {
            { (IpConnectionsPrefixes), typeof(IpCameraCapture) },
            { (IpConnectionsSuffixes), typeof(IpCameraCapture) },
            { (AndroidCamera2CapturePrefixes), typeof(AndroidCamera2Capture) }
        };
    }
}
