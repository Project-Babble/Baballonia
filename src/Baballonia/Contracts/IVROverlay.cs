using System;
using System.Threading.Tasks;

namespace Baballonia.Contracts;

public interface IVROverlay : IDisposable
{
    public Task<(bool success, string status)> EyeTrackingCalibrationRequested(string calibrationRoutine);
}
