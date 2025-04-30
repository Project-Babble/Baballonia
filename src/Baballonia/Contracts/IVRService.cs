﻿using Baballonia.Services;
using System.Threading.Tasks;
using Baballonia.Models;
using Baballonia.Services.Overlay;

namespace Baballonia.Contracts;

internal interface IVrService
{
    public Task<bool> StartOverlay(string[] arguments = null, string[] blacklistedPrograms = null);
    public Task<VrCalibrationStatus> GetStatusAsync();

    public Task<bool> StartCamerasAsync(VrCalibration calibration);

    public Task<bool> StartCalibrationAsync(VrCalibration calibration);

    public Task<bool> StartPreviewAsync(VrCalibration calibration);

    public Task<bool> StopPreviewAsync();
    public Task<bool> StartTrainer(string[] arguments = null, string[] blacklistedPrograms = null);
    public bool StopOverlay();
    public bool StopTrainer();
    public void StopAllProcesses();
}
