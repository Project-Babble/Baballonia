using System;
using System.Threading.Tasks;
using Baballonia.Helpers;

namespace Baballonia.Contracts;

public interface IVROverlay : IDisposable
{
    public Task<(bool success, string status)> EyeTrackingCalibrationRequested(CalibrationRoutine.Routines calibrationRoutine);
}
