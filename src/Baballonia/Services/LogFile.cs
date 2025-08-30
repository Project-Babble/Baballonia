using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class LogFileLogger(string categoryName, StreamWriter file) : ILogger
{
    private static readonly Mutex Mutex = new ();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel)
    {
        var minLogLevel = GetMinimumLogLevel();
        return logLevel >= minLogLevel;
    }

    public static LogLevel GetMinimumLogLevel()
    {
        try
        {
            // Hack to get settings key without the settings service
            var settingsPath = Path.Combine(Utils.PersistentDataDirectory, "ApplicationData", "LocalSettings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<JsonDocument>(json);

                if (settings?.RootElement.TryGetProperty("AppSettings_LogLevel", out var logLevelElement) == true)
                {
                    var logLevelString = logLevelElement.GetString();
                    if (Enum.TryParse<LogLevel>(logLevelString, true, out var logLevel))
                    {
                        return logLevel;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors, use default
        }

        // Default to Debug level
        return LogLevel.Debug;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        Mutex.WaitOne(); // Wait for the semaphore to be released
        try
        {
            file.Write($"[{categoryName}][{DateTime.Now}] {logLevel}: {formatter(state, exception)}\n");
            file.Flush();
        }
        catch
        {
            // Ignore cus sandboxing causes a lot of issues here
        }
        finally
        {
            Mutex.ReleaseMutex();
        }
    }
}
