using Baballonia.Factories;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services.Inference;

public class SingleCameraSourceFactory
{
    public SingleCameraSource? Create(string address)
    {
        var logger = Ioc.Default.GetService<ILogger<SingleCameraSource>>()!;
        var platform = new PlatformConnectorFactory().Create(logger, address);
        if(platform != null)
            return new SingleCameraSource(logger, platform, address);

        return null;
    }
}
