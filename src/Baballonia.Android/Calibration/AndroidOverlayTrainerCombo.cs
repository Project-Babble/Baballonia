using System;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Helpers;
namespace Baballonia.Android.Calibration;

public class AndroidOverlayTrainerCombo : IVROverlay, IVRCalibrator, IDisposable
{
    public Task EyeTrackingCalibrationRequested(string calibrationRoutine)
    {
        return Task.CompletedTask;
    }

    public Task<(bool success, string status)> EyeTrackingCalibrationRequested(CalibrationRoutine.Routines calibrationRoutine)
    {
        return Task.FromResult((true, "Not Supported"));
    }

    public void Dispose()
    {

    }
}
