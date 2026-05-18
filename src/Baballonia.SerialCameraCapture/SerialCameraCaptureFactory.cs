using Baballonia.SDK;
using Microsoft.Extensions.Logging;

namespace Baballonia.SerialCameraCapture;

public class SerialCameraCaptureFactory(ILoggerFactory loggerFactory) : ICaptureFactory
{
    public Capture Create(string address)
    {
        return new SerialCameraCapture(address, loggerFactory.CreateLogger<SerialCameraCapture>());
    }

    public bool CanConnect(string address)
    {
        var lowered = address.ToLower();
        return lowered.StartsWith("com") ||
               lowered.StartsWith("/dev/tty") ||
               lowered.StartsWith("/dev/cu") ||
               lowered.StartsWith("/dev/ttyacm");
    }

    public string GetProviderName() => "Serial Camera";
}
