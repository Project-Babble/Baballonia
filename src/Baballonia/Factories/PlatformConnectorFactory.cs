using System;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;

namespace Baballonia.Factories;

public class PlatformConnectorFactory
{
    public PlatformConnector Create(ILogger logger, string cameraAddress)
    {
        return (PlatformConnector)Activator.CreateInstance(App.PlatformConnectorType, cameraAddress, logger, null)!;
    }
}
