using System;

namespace Baballonia.Services.Overlay;

public class ProcessOutputEventArgs : EventArgs
{
    public string Output { get; }
    public bool IsError { get; }
    public DateTime Timestamp { get; }

    public ProcessOutputEventArgs(string output, bool isError)
    {
        Output = output;
        IsError = isError;
        Timestamp = DateTime.Now;
    }
}
