using Baballonia.SDK;
using Microsoft.Extensions.Logging;

namespace Baballonia.OpenCVCapture;

public class OpenCvCaptureFactory(ILoggerFactory loggerFactory) : ICaptureFactory
{
    public Capture Create(string address)
    {
        return new OpenCvCapture(address, loggerFactory.CreateLogger<OpenCvCapture>());
    }

    public bool CanConnect(string address)
    {
        var lowered = address.ToLower();
        var serial = lowered.StartsWith("com") ||
                     lowered.StartsWith("/dev/tty") ||
                     lowered.StartsWith("/dev/cu") ||
                     lowered.StartsWith("/dev/ttyacm");;
        if (serial) return false;

        return lowered.StartsWith("/dev/video") ||
               lowered.EndsWith("appsink") ||
               address == "HTC Multimedia Camera" ||
               int.TryParse(address, out _) ||
               IsSupportedStreamUrl(address);
    }

    // Network streams OpenCV can open through its FFMPEG/GStreamer backends. An explicit allowlist
    // rather than "any absolute URI" so non-capture schemes (file://, ftp://, …) aren't claimed and
    // then left to fail out the caller's frame timeout.
    private static readonly HashSet<string> StreamSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "rtsp", "rtsps", "rtmp", "rtmps", "rtp", "udp", "tcp", "mms", "mmsh", "mmst",
    };

    private static bool IsSupportedStreamUrl(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && StreamSchemes.Contains(uri.Scheme);

    public string GetProviderName() => "Normal Camera";
}
