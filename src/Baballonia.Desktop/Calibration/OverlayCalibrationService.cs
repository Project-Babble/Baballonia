using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.CaptureBin.IO;
using Baballonia.Contracts;
using Baballonia.Desktop.Calibration.Aero;
using Baballonia.Desktop.Trainer;
using Baballonia.Helpers;
using Baballonia.Services;
using Baballonia.Services.events;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OverlaySDK;
using OverlaySDK.Packets;

namespace Baballonia.Desktop.Calibration;

public class OverlayTrainerService(
    ILogger<OverlayTrainerService> logger,
    IOverlayProgram overlayProgram,
    ILocalSettingsService localSettingsService,
    EyePipelineManager eyePipelineManager,
    EyeCalibration eyeCalibration,
    DataUploaderService dataUploaderService)
    : IVROverlay, IDisposable
{

    private readonly CancellationTokenSource _tokenSource = new();

    public void Dispose()
    {
        overlayProgram.Dispose();
    }

    public async Task<(bool success, string status)> EyeTrackingCalibrationRequested(
        CalibrationRoutine.Routines routine)
    {
        if (!overlayProgram.CanStart())
        {
            return (false, "Cannot start Overlay");
        }

        overlayProgram.Start();

        await Task.Delay(TimeSpan.FromSeconds(0.25));

        var overlayLogger = new OverlayLogger(logger);

        var sfactory = new SocketFactory();
        var sock = sfactory.CreateServer("127.0.0.1", 2425);
        overlayLogger.Info("Accepted connection");

        var tcp = new EventDrivenTcpClient(sock);
        var client = new EventDrivenJsonClient(tcp);

        var messageDispatcher = new OverlayMessageDispatcher(overlayLogger, client);

        if (!Directory.Exists(Utils.ModelDataDirectory)) Directory.CreateDirectory(Utils.ModelDataDirectory);
        if (!Directory.Exists(Utils.ModelsDirectory)) Directory.CreateDirectory(Utils.ModelsDirectory);

        var steps = routine switch
        {
            CalibrationRoutine.Routines.BasicCalibration => eyeCalibration.BasicAllCalibration(),
            CalibrationRoutine.Routines.BasicCalibrationNoTutorial => eyeCalibration.BasicAllCalibrationQuick(),
            _ => eyeCalibration.BasicAllCalibration()
        };
        foreach (var calibrationStep in steps)
        {
            await calibrationStep.ExecuteAsync(messageDispatcher, _tokenSource.Token);
        }

        var srcPath = Path.Combine(Utils.ModelDataDirectory, "tuned_temporal_eye_tracking_latest.onnx");
        var destPath = Path.Combine(Utils.ModelsDirectory,
            $"tuned_temporal_eye_tracking_{DateTime.Now:yyyyMMdd_HHmmss}.onnx");

        File.Move(srcPath, destPath);

        localSettingsService.SaveSetting("EyeHome_EyeModel", destPath);
        await eyePipelineManager.LoadInferenceAsync();

        if (localSettingsService.ReadSetting<bool>("AppSettings_ShareEyeData"))
        {
            await dataUploaderService.UploadDataAsync(destPath);
        }

        await overlayProgram.WaitForExitAsync();

        return (true, string.Empty);
    }
}
