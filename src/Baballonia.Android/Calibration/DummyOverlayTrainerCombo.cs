using System;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Services.Inference;
using Baballonia.ViewModels.SplitViewPane;

namespace Baballonia.Android.Calibration;

public class DummyOverlayTrainerCombo : IVROverlay, IVRCalibrator, IDisposable
{
    public Task EyeTrackingCalibrationRequested(string calibrationRoutine)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {

    }
}
