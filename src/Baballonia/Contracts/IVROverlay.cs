using System.Threading.Tasks;
using Baballonia.Helpers;
using Baballonia.Models;
using Baballonia.Services.Inference;
using Baballonia.ViewModels.SplitViewPane;

namespace Baballonia.Contracts;

public interface IVROverlay
{
    public Task EyeTrackingCalibrationRequested(string calibrationRoutine);

}
