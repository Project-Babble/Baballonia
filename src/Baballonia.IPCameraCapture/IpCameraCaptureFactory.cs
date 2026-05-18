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
        return Uri.TryCreate(address, UriKind.RelativeOrAbsolute, out _) && address.StartsWith("http://");
    }

    public string GetProviderName() => "Wireless/IP Camera";
}
