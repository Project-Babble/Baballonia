using Baballonia.Factories;
using Baballonia.Services.Inference.VideoSources;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services.Inference;

public static class SingleCameraSourceFactory
{
    public static SingleCameraSource? Create(string address, string preferredCapture = "")
    {
        var logger = Ioc.Default.GetService<ILogger<SingleCameraSource>>()!;
        var platform = PlatformConnectorFactory.Create(logger, address);
        return platform != null ? new SingleCameraSource(logger, platform, address, preferredCapture) : null;
    }
}
