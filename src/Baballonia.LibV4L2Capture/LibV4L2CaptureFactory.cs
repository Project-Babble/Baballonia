using Baballonia.SDK;
using Microsoft.Extensions.Logging;

namespace Baballonia.LibV4L2Capture;

public class LibV4L2CaptureFactory : ICaptureFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public LibV4L2CaptureFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public Capture Create(string address)
    {
        return new LibV4L2Capture(address, _loggerFactory.CreateLogger<LibV4L2Capture>());
    }

    public bool CanConnect(string address) {
        return address.StartsWith("/dev/video");
    }

    public string GetProviderName()
    {
        return "Video4Linux2";
    }
}
