using Baballonia.SDK;
using Microsoft.Extensions.Logging;

namespace Baballonia.IPCameraCapture;

public class IpCameraCaptureFactory(ILoggerFactory loggerFactory) : ICaptureFactory
{
    public Capture Create(string address)
    {
        return new IpCameraCapture(address, loggerFactory.CreateLogger<IpCameraCapture>());
    }

    public bool CanConnect(string address)
    {
        // MJPEG-over-HTTP(S). The parser streams via HttpClient, which handles TLS, so https works
        // too. Scheme-only match (not rtsp/rtmp/etc.) — those carry codecs this raw JPEG parser
        // can't decode and belong to the OpenCV backend instead.
        return Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public string GetProviderName() => "Wireless/IP Camera";
}
