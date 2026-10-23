using System;
using System.Timers;

namespace Baballonia.Helpers;

public class DebounceFunction
{
    private readonly Timer _timer;
    private readonly Action _action;

    public DebounceFunction(Action action, int debounceMilliseconds = 5000)
    {
        _action = action;
        _timer = new Timer(debounceMilliseconds);
        _timer.AutoReset = false;
        _timer.Elapsed += (s, e) => _action();
    }

    public void Call()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Force()
    {
        _timer.Stop();
        _action();
    }

    public void Cancel()
    {
        _timer.Stop();
    }
}
