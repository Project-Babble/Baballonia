using Avalonia.Threading;
using Baballonia.Contracts;
using System;

namespace Baballonia.Services;

// Simple service to invoke actions on the UI thread from the Core project.
public class DispatcherService : IDispatcherService
{
    public void Run(Action action) => Dispatcher.UIThread.Invoke(action);
}
