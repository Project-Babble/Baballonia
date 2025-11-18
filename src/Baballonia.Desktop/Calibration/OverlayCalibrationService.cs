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

public class OverlayTrainerService : IVROverlay, IDisposable
{
    private ILocalSettingsService _localSettingsService;
    private ILogger<OverlayTrainerService> _logger;
    private IOverlayProgram _program;

    private EyePipelineManager _eyePipelineManager;
    private readonly EyeCalibration _eyeCalibration;
    private readonly CancellationTokenSource _tokenSource = new();


    public OverlayTrainerService(ILogger<OverlayTrainerService> logger, IOverlayProgram overlayProgram,
        ILocalSettingsService localSettingsService, EyePipelineManager eyePipelineManager,
        EyeCalibration eyeCalibration)
    {
        _logger = logger;
        _program = overlayProgram;
        _localSettingsService = localSettingsService;
        _eyePipelineManager = eyePipelineManager;
        _eyeCalibration = eyeCalibration;
    }

    public void Dispose()
    {
        _program.Dispose();
    }

    public async Task<(bool success, string status)> EyeTrackingCalibrationRequested(
        CalibrationRoutine.Routines routine)
    {
        if (!_program.CanStart())
        {
            return (false, "Cannot start Overlay");
        }

        _program.Start();

        await Task.Delay(TimeSpan.FromSeconds(0.25));

        var logger = new OverlayLogger(_logger);

        var sfactory = new SocketFactory();
        var sock = sfactory.CreateServer("127.0.0.1", 2425);
        logger.Info("Accepted connection");

        var tcp = new EventDrivenTcpClient(sock);
        var client = new EventDrivenJsonClient(tcp);

        var messageDispatcher = new OverlayMessageDispatcher(logger, client);

        if (!Directory.Exists(Utils.ModelDataDirectory)) Directory.CreateDirectory(Utils.ModelDataDirectory);
        if (!Directory.Exists(Utils.ModelsDirectory)) Directory.CreateDirectory(Utils.ModelsDirectory);

        var steps = routine switch
        {
            CalibrationRoutine.Routines.BasicCalibration => _eyeCalibration.BasicAllCalibration(),
            CalibrationRoutine.Routines.BasicCalibrationNoTutorial => _eyeCalibration.BasicAllCalibrationQuick(),
            _ => _eyeCalibration.BasicAllCalibration()
        };
        foreach (var calibrationStep in steps)
        {
            await calibrationStep.ExecuteAsync(messageDispatcher, _tokenSource.Token);
        }

        var srcPath = Path.Combine(Utils.ModelDataDirectory, "tuned_temporal_eye_tracking_latest.onnx");
        var destPath = Path.Combine(Utils.ModelsDirectory,
            $"tuned_temporal_eye_tracking_{DateTime.Now:yyyyMMdd_HHmmss}.onnx");

        File.Move(srcPath, destPath);

        _localSettingsService.SaveSetting("EyeHome_EyeModel", destPath);
        await _eyePipelineManager.LoadInferenceAsync();

        await _program.WaitForExitAsync();

        return (true, string.Empty);
    }
}
