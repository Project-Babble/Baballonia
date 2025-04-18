using System;
using System.Collections.Concurrent;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace AvaloniaMiaDev.Services;

public sealed class OutputLogProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, OutputPageLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dispatcher _dispatcher;

    public OutputLogProvider(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new OutputPageLogger(name, _dispatcher));

    public void Dispose() => _loggers.Clear();
}
