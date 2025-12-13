using Baballonia.SDK;
using Microsoft.Extensions.Logging;

namespace Baballonia.OpenCVCapture;

public class OpenCvCaptureFactory : ICaptureFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public OpenCvCaptureFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public Capture Create(string address)
    {
        return new OpenCvCapture(address, _loggerFactory.CreateLogger<OpenCvCapture>());
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
               int.TryParse(address, out _) ||
               Uri.TryCreate(address, UriKind.Absolute, out _);
    }

    public string GetProviderName()
    {
        return nameof(OpenCvCapture);
    }
}
