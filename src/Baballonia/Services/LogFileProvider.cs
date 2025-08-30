using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Baballonia.Services;

[ProviderAlias("Debug")]
public class LogFileProvider : ILoggerProvider
{
    private readonly StreamWriter? _writer;
    private const int MaxLogs = 10;

    public LogFileProvider()
    {
        if (!Directory.Exists(Utils.UserAccessibleDataDirectory)) // Eat my ass windows
            Directory.CreateDirectory(Utils.UserAccessibleDataDirectory);

        CleanupOldLogFiles();

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var logFileName = $"baballonia_desktop.{timestamp}.log";
        var logPath = Path.Combine(Utils.UserAccessibleDataDirectory, logFileName);

        var file = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096,
            FileOptions.WriteThrough);
        _writer = new StreamWriter(file);
    }

    private void CleanupOldLogFiles()
    {
        try
        {
            var logFiles = Directory.GetFiles(Utils.UserAccessibleDataDirectory, "baballonia_desktop.*.log")
                .Select(file => new FileInfo(file))
                .OrderByDescending(fi => fi.CreationTime)
                .ToList();

            if (logFiles.Count >= MaxLogs)
            {
                var filesToDelete = logFiles.Skip(MaxLogs - 1);
                foreach (var fileInfo in filesToDelete)
                {
                    try
                    {
                        File.Delete(fileInfo.FullName);
                    }
                    catch
                    {
                        // Ignore errors when deleting old log files
                    }
                }
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }

    private readonly ConcurrentDictionary<string, LogFileLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    public ILogger CreateLogger(string categoryName)
    {
        if (_writer != null)
        {
            return _loggers.GetOrAdd(categoryName, name => new LogFileLogger(name, _writer));
        }

        return NullLogger.Instance;
    }

    public void Dispose()
    {
        _loggers.Clear();
        _writer?.Dispose();
    }
}
