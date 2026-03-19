using Microsoft.Extensions.Logging;
using System;

namespace Baballonia.Desktop.Calibration;

public class OverlayLogger(ILogger logger) : OverlaySDK.ILogger
{
    public void Debug(string message) => logger.LogDebug(message);
    public void Info(string message) => logger.LogInformation(message);
    public void Warn(string message) => logger.LogWarning(message);
    public void Error(string message, Exception? ex = null) => logger.LogError(ex, message);
}
