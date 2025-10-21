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
    private IEyePipelineEventBus _eyePipelineEventBus;
    private ITrainerService _trainerService;
    private IOverlayProgram _program;
    private OverlayMessageDispatcher _messageDispatcher;

    private EyePipelineManager _eyePipelineManager;
    private readonly EyeCalibration _eyeCalibration;
    private readonly CancellationTokenSource _tokenSource = new();


    public OverlayTrainerService(ILogger<OverlayTrainerService> logger, IEyePipelineEventBus eyePipelineEventBus,
        ITrainerService trainerService, IOverlayProgram overlayProgram, ILocalSettingsService localSettingsService, EyePipelineManager eyePipelineManager, EyeCalibration eyeCalibration)
    {
        _logger = logger;
        _eyePipelineEventBus = eyePipelineEventBus;
        _trainerService = trainerService;
        _program = overlayProgram;
        _localSettingsService = localSettingsService;
        _eyePipelineManager = eyePipelineManager;
        _eyeCalibration = eyeCalibration;
    }

    public void Dispose()
    {
        _program.Dispose();
    }

    public async Task<(bool success, string status)> EyeTrackingCalibrationRequested(string calibrationRoutine)
    {
        if (!int.TryParse(calibrationRoutine, out var r)) return (false, "Something went horribly wrong");

        if (!_program.CanStart())
        {
            return (false, "Cannot start Overlay");
        }

        _program.Start();

        var logger = new OverlayLogger(_logger);

        var sfactory = new SocketFactory();
        var sock = sfactory.CreateServer("127.0.0.1", 2425);
        logger.Info("Accepted connection");

        var tcp = new EventDrivenTcpClient(sock);
        var client = new EventDrivenJsonClient(tcp);

        _messageDispatcher = new OverlayMessageDispatcher(logger, client);

        if (!Directory.Exists(Utils.ModelDataDirectory)) Directory.CreateDirectory(Utils.ModelDataDirectory);
        if (!Directory.Exists(Utils.ModelsDirectory)) Directory.CreateDirectory(Utils.ModelsDirectory);

        var steps = _eyeCalibration.BasicAllCalibration();
        foreach (var calibrationStep in steps)
        {
            await calibrationStep.ExecuteAsync(_messageDispatcher, _tokenSource.Token);
        }

        var destPath = Path.Combine(Utils.ModelsDirectory,
            $"tuned_temporal_eye_tracking_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.onnx");

        _localSettingsService.SaveSetting("EyeHome_EyeModel", destPath);
        await _eyePipelineManager.LoadInferenceAsync();

        await _program.WaitForExitAsync();

        return (true, string.Empty);
    }



}
