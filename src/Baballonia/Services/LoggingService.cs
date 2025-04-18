using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace AvaloniaMiaDev.Services;

public class OutputPageLogger : ILogger
{
    private readonly string _categoryName;
    public static readonly ObservableCollection<string> FilteredLogs = new();
    public static readonly ObservableCollection<string> AllLogs = new();
    private static Dispatcher? _dispatcher;

    public OutputPageLogger(string categoryName, Dispatcher? queue)
    {
        _categoryName = categoryName;
        _dispatcher = queue;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Add to the staticLog from the dispatcher thread
        _dispatcher!.Post(() =>
        {
            AllLogs.Add($"[{_categoryName}] {logLevel}: {formatter(state, exception)}");
            // Filtered is what the user sees, so show Information scope
            if (logLevel >= LogLevel.Information)
            {
                FilteredLogs.Add($"[{_categoryName}] {logLevel}: {formatter(state, exception)}");
            }
        }, DispatcherPriority.Background);
    }
}
