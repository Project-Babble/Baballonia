using Baballonia.SDK;
using Microsoft.Extensions.Logging;

namespace Baballonia.VFTCapture;

public class VFTCaptureFactory : ICaptureFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public VFTCaptureFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public Capture Create(string address)
    {
        return new VftCapture(address, _loggerFactory.CreateLogger<VftCapture>());
    }

    public bool CanConnect(string address)
    {
        var lowered = address.ToLower();
        return lowered.StartsWith("/dev/video") && OperatingSystem.IsLinux();
    }

    public string GetProviderName()
    {
        return nameof(VFTCapture);
    }
}
