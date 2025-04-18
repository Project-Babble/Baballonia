using AvaloniaMiaDev.Services;
using System.Threading.Tasks;
using AvaloniaMiaDev.Models;

namespace AvaloniaMiaDev.Contracts;

internal interface IVRService
{
    public Task<VrCalibrationStatus> GetStatusAsync();

    public Task<bool> StartCamerasAsync(VrCalibration calibration);

    public Task<bool> StartCalibrationAsync(VrCalibration calibration);

    public Task<bool> StartPreviewAsync(VrCalibration calibration);

    public Task<bool> StopPreviewAsync();
}
