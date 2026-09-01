using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Services;
using Microsoft.Extensions.Logging;
using OverlaySDK;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OverlaySDK.Packets;

namespace Baballonia.Desktop.Calibration;

public class OverlayTrainerService(
    ILogger<OverlayTrainerService> logger,
    IOverlayProgram overlayProgram,
    ILocalSettingsService localSettingsService,
    EyePipelineManager eyePipelineManager,
    EyeCalibration eyeCalibration,
    DataUploaderService dataUploaderService)
    : IVROverlay
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

        var overlayLogger = new OverlayLogger(logger);

        var sfactory = new SocketFactory();
        // Start binding/listening before launching Godot. CreateServer waits for the client, so run
        // it in the background; this removes the first-launch race caused by the old fixed delay.
        var serverTask = Task.Run(() => sfactory.CreateServer("127.0.0.1", 2425), _tokenSource.Token);

        overlayProgram.Start();

        var sock = await serverTask.WaitAsync(TimeSpan.FromSeconds(30), _tokenSource.Token);
        overlayLogger.Info("Accepted connection");

        var tcp = new EventDrivenTcpClient(sock);
        var client = new EventDrivenJsonClient(tcp);

        var messageDispatcher = new OverlayMessageDispatcher(overlayLogger, client);

        var readyHandler = new OverlayReadyHandler();
        messageDispatcher.RegisterHandler(readyHandler);
        await readyHandler.WaitAsync(_tokenSource.Token);
        messageDispatcher.UnRegisterHandler(readyHandler);
        overlayLogger.Info("Calibration overlay is ready");

        if (!Directory.Exists(Utils.ModelDataDirectory)) Directory.CreateDirectory(Utils.ModelDataDirectory);
        if (!Directory.Exists(Utils.ModelsDirectory)) Directory.CreateDirectory(Utils.ModelsDirectory);

        var steps = routine switch
        {
            CalibrationRoutine.Routines.BasicCalibration => eyeCalibration.BasicAllCalibration(),
            CalibrationRoutine.Routines.BasicCalibrationNoTutorial => eyeCalibration.BasicAllCalibrationQuick(),
            CalibrationRoutine.Routines.GazeOnly => eyeCalibration.GazeCalibration(),
            CalibrationRoutine.Routines.BlinkOnly => eyeCalibration.BlinkCalibration(),
            _ => eyeCalibration.BasicAllCalibration()
        };
        foreach (var calibrationStep in steps)
        {
            logger.LogInformation("Starting calibration step {Step}", calibrationStep.Name);
            await calibrationStep.ExecuteAsync(messageDispatcher, _tokenSource.Token);
            logger.LogInformation("Finished calibration step {Step}", calibrationStep.Name);
        }

        var srcPath = Path.Combine(Utils.ModelDataDirectory, "tuned_temporal_eye_tracking_latest.onnx");
        var destPath = Path.Combine(Utils.ModelsDirectory,
            $"tuned_temporal_eye_tracking_{DateTime.Now:yyyyMMdd_HHmmss}.onnx");

        File.Move(srcPath, destPath);

        localSettingsService.SaveSetting("EyeHome_EyeModel", destPath);
        await eyePipelineManager.LoadInferenceAsync();

        if (localSettingsService.ReadSetting<bool>("AppSettings_ShareEyeData"))
        {
            var userCal = Path.Combine(Utils.ModelDataDirectory, "user_cal.bin");
            await dataUploaderService.UploadDataAsync(userCal);
        }

        await overlayProgram.WaitForExitAsync();

        return (true, string.Empty);
    }

    private sealed class OverlayReadyHandler : PacketHandlerAdapter
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitAsync(CancellationToken cancellationToken) =>
            _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        public override void OnRoutineFinishedPacket(RoutineFinishedPacket packet)
        {
            if (string.Equals(packet.RoutineName, "ready", StringComparison.OrdinalIgnoreCase))
                _ready.TrySetResult();
        }
    }
}
