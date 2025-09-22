using System;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;

namespace Baballonia.Factories;

public static class PlatformConnectorFactory
{
    public static PlatformConnector Create(ILogger logger, string cameraAddress) =>
        (PlatformConnector)Activator.CreateInstance(App.PlatformConnectorType, cameraAddress, logger)!;
}
