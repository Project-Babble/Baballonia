using System;

namespace Baballonia.Desktop.Calibration.Aero.Overlay;

public class ProcessOutputEventArgs(string output, bool isError) : EventArgs
{
    public string Output { get; } = output;
    public bool IsError { get; } = isError;
    public DateTime Timestamp { get; } = DateTime.Now;
}
