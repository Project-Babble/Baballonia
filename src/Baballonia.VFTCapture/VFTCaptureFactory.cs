using Baballonia.SDK;
using Baballonia.VFTCapture.Linux;
using Baballonia.VFTCapture.Windows;
using Microsoft.Extensions.Logging;

namespace Baballonia.VFTCapture;

public class VFTCaptureFactory(ILoggerFactory loggerFactory) : ICaptureFactory
{
    public Capture Create(string address)
    {
        if (OperatingSystem.IsWindows())
            return new WindowsVftCapture(address, loggerFactory.CreateLogger<WindowsVftCapture>());

        if (OperatingSystem.IsLinux())
            return new LinuxVftCapture(address, loggerFactory.CreateLogger<LinuxVftCapture>());

        throw new InvalidOperationException("Unsupported operating system for VFTCapture.");
    }

    public bool CanConnect(string address)
    {
        var lowered = address.ToLower();

        if (OperatingSystem.IsWindows())
        {
            return !lowered.StartsWith("com");
        }
        else if (OperatingSystem.IsLinux())
        {
            return lowered.StartsWith("/dev/video");
        }

        return false;
    }

    public string GetProviderName() => nameof(VFTCapture);
}
