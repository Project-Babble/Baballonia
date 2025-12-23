using System;
using System.Threading.Tasks;

namespace Baballonia.Desktop.Calibration;

public interface IOverlayProgram : IDisposable
{
    bool CanStart();
    void Start();
    Task WaitForExitAsync();
}
