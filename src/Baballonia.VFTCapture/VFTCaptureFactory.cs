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
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        if (OperatingSystem.IsLinux())
        {
            var lowered = address.ToLower();
            return lowered.StartsWith("/dev/video");
        }

        return false;
    }

    public string GetProviderName()
    {
        return nameof(VFTCapture);
    }
}
