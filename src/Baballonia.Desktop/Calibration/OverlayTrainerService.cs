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

public class OverlayTrainerService : PacketHandlerAdapter, IVROverlay
{
    private enum OverlayState
    {
        Tutorial,
        Gaze,
        Blink,
    }

    private LocalSettingsService _localSettingsService;
    private ILogger<OverlayTrainerService> _logger;
    private IEyePipelineEventBus _eyePipelineEventBus;
    private ITrainerService _trainerService;
    private IOverlayProgram _program;
    private OverlayState _currentState = OverlayState.Tutorial;
    private OverlayMessageDispatcher _messageDispatcher;

    private object _frame_lock = new();
    private List<Frame> _currentFrames = new();
    private HmdPositionalDataPacket? _latesdPosData;
    private Stopwatch _posDataStopwatch = Stopwatch.StartNew();
    private EyePipelineManager _eyePipelineManager;


    public OverlayTrainerService(ILogger<OverlayTrainerService> logger, IEyePipelineEventBus eyePipelineEventBus,
        ITrainerService trainerService, IOverlayProgram overlayProgram, LocalSettingsService localSettingsService, EyePipelineManager eyePipelineManager)
    {
        _logger = logger;
        _eyePipelineEventBus = eyePipelineEventBus;
        _trainerService = trainerService;
        _program = overlayProgram;
        _localSettingsService = localSettingsService;
        _eyePipelineManager = eyePipelineManager;
    }

    public void Dispose()
    {
        _eyePipelineEventBus.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(HandleEyeImageEvent);
        _program.Dispose();
    }

    public async Task<(bool success, string status)> EyeTrackingCalibrationRequested(string calibrationRoutine)
    {
        if (!int.TryParse(calibrationRoutine, out var r)) return (false, "Something went horribly wrong");
        var rout = (CalibrationRoutine.Routines)r;

        lock (_frame_lock)
        {
            _currentFrames.Clear();
        }

        if (!_program.CanStart())
        {
            return (false, "Cannot start Overlay");
        }

        _program.Start();

        _eyePipelineEventBus.Subscribe<EyePipelineEvents.NewTransformedFrameEvent>(HandleEyeImageEvent);

        var logger = new OverlayLogger(_logger);

        var sfactory = new SocketFactory();
        var sock = sfactory.CreateServer("127.0.0.1", 2425);
        logger.Info("Accepted connection");

        var tcp = new EventDrivenTcpClient(sock);
        var client = new EventDrivenJsonClient(tcp);

        _messageDispatcher = new OverlayMessageDispatcher(logger, client);
        _messageDispatcher.RegisterHandler(this);

        if (rout is CalibrationRoutine.Routines.GazeOnly or CalibrationRoutine.Routines.BasicCalibration
            or CalibrationRoutine.Routines.BasicCalibrationNoTutorial)
        {
            if (rout is CalibrationRoutine.Routines.BasicCalibration)
                await FixedLengthWithDelayedState(28, OverlayState.Tutorial, "gazetutorial", 0);
            else
                await FixedLengthWithDelayedState(10, OverlayState.Tutorial, "gazetutorialshort", 0);

            await VariableLengthWithDelayedState(120, OverlayState.Gaze, "gaze");

            WriteBinAndClear("gaze.bin");
        }

        if (rout is CalibrationRoutine.Routines.BlinkOnly or CalibrationRoutine.Routines.BasicCalibration
            or CalibrationRoutine.Routines.BasicCalibrationNoTutorial)
        {
            await VariableLengthWithDelayedState(10, OverlayState.Tutorial, "blinktutorial", 0);

            await VariableLengthWithDelayedState(20, OverlayState.Blink, "blink");

            WriteBinAndClear("blink.bin");
        }

        MergeBins("user_cal.bin", "gaze.bin", "blink.bin");

        _eyePipelineEventBus.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(HandleEyeImageEvent);

        _messageDispatcher.Dispatch(new RunFixedLenghtRoutinePacket("trainer"));

        _trainerService.OnProgress += packet => { _messageDispatcher.Dispatch(packet); };

        if (!Directory.Exists(Utils.ModelsDirectory))
        {
            Directory.CreateDirectory(Utils.ModelsDirectory);
        }
        var destPath = Path.Combine(Utils.ModelsDirectory,
            $"tuned_temporal_eye_tracking_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.onnx");
        _trainerService.RunTraining(Path.Combine(Utils.ModelsDirectory, "user_cal.bin"), destPath);

        await _trainerService.WaitAsync();
        _localSettingsService.SaveSetting("EyeHome_EyeModel", destPath);
        await _eyePipelineManager.LoadInferenceAsync();

        _messageDispatcher.Dispatch(new RunFixedLenghtRoutinePacket("close"));

        await _program.WaitForExitAsync();

        return (true, string.Empty);
    }

    void MergeBins(string result, params string[] inputs)
    {
        var resultPath = Path.Combine(Utils.ModelsDirectory, result);
        var inputPaths = inputs.Select(i => Path.Combine(Utils.ModelsDirectory, i)).ToArray();
        CaptureBin.IO.CaptureBin.Concatenate(resultPath, inputPaths);
    }
    void WriteBinAndClear(string name)
    {
        List<Frame> framesCopy;
        lock (_frame_lock)
        {
            framesCopy = new List<Frame>(_currentFrames);
            _currentFrames.Clear();
        }

        CaptureBin.IO.CaptureBin.WriteAll(Path.Combine(Utils.ModelsDirectory, name), framesCopy);
    }

    async Task FixedLengthWithDelayedState(float durationSeconds, OverlayState state, string routine,
        float buffer = 0.1f)
    {
        _messageDispatcher.Dispatch(new RunFixedLenghtRoutinePacket(routine));
        await DelayedState(durationSeconds, state, buffer);
    }

    async Task VariableLengthWithDelayedState(float durationSeconds, OverlayState state, string routine,
        float buffer = 0.1f)
    {
        _messageDispatcher.Dispatch(new RunVariableLenghtRoutinePacket(routine, TimeSpan.FromSeconds(durationSeconds)));
        await DelayedState(durationSeconds, state, buffer);
    }

    async Task DelayedState(float durationSeconds, OverlayState state, float buffer = 0.1f)
    {
        if (buffer > 0) await Task.Delay(TimeSpan.FromSeconds(buffer));

        _currentState = state;

        await Task.Delay(TimeSpan.FromSeconds(durationSeconds - buffer));
    }

    private void HandleEyeImageEvent(EyePipelineEvents.NewTransformedFrameEvent e)
    {
        if (_currentState is OverlayState.Tutorial) return;

        var image = e.image;
        var channels = image.Channels();
        if (channels != 2)
            return;

        var images = image.Split();
        HandleNewImageData(images[0], images[1]);
    }

    void HandleNewImageData(Mat left, Mat right)
    {
        if (_latesdPosData == null)
            return;
        if (_currentState == OverlayState.Gaze && _posDataStopwatch.ElapsedMilliseconds > 100)
            return;

        const int jpegQuality = 50;

        Cv2.ImEncode(".jpg", left, out var bufLeft, [(int)ImwriteFlags.JpegQuality, jpegQuality]);
        Cv2.ImEncode(".jpg", right, out var bufRight, [(int)ImwriteFlags.JpegQuality, jpegQuality]);

        uint routineState = _currentState switch
        {
            OverlayState.Gaze => CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_GOOD_DATA,
            OverlayState.Blink => CaptureFlags.FLAG_RESTING | CaptureFlags.FLAG_GOOD_DATA,
            _ => 0
        };

        var frame = new Frame
        {
            Header = GenerateHeader(_latesdPosData) with
            {
                RoutineState = routineState,
                JpegDataLeftLength = (uint)bufLeft.Length,
                JpegDataRightLength = (uint)bufRight.Length
            },
            LeftJpeg = bufLeft, RightJpeg = bufRight
        };

        lock (_frame_lock)
        {
            _currentFrames.Add(frame);
        }
    }

    CaptureFrameHeader GenerateHeader(HmdPositionalDataPacket positionalData)
    {
        var time = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();
        return new CaptureFrameHeader
        {
            LeftEyePitch = positionalData.LeftEyePitch,
            LeftEyeYaw = positionalData.LeftEyeYaw,
            RightEyePitch = positionalData.RightEyePitch,
            RightEyeYaw = positionalData.RightEyeYaw,
            RoutinePitch = positionalData.RoutinePitch,
            RoutineYaw = positionalData.RoutineYaw,
            RoutineDistance = positionalData.RoutineDistance,
            RoutineConvergence = positionalData.RoutineConvergence,
            FovAdjustDistance = positionalData.FovAdjustDistance,
            Timestamp = time,
            TimestampLeft = time,
            TimestampRight = time,
        };
    }

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        _posDataStopwatch.Restart();
        Interlocked.Exchange(ref _latesdPosData, positionalData);
    }

}
