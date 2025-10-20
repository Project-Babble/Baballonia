using Baballonia.IPCameraCapture;
using Baballonia.SDK;
using Microsoft.Extensions.Logging;

namespace Baballonia.Android.Captures;

public class IpCameraCaptureFactory : ICaptureFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public IpCameraCaptureFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public Capture Create(string address)
    {
        return new IpCameraCapture(address, _loggerFactory.CreateLogger<IpCameraCapture>());
    }

    public bool CanConnect(string address)
    {
        return Uri.TryCreate(address, UriKind.RelativeOrAbsolute, out _) && address.StartsWith("http://");
    }

    public string GetProviderName()
    {
        return nameof(IpCameraCapture);
    }
}
