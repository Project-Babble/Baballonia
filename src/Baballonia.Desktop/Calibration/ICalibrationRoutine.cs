using Baballonia.CaptureBin.IO;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.Services.events;
using OpenCvSharp;
using OverlaySDK;
using OverlaySDK.Packets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Desktop.Calibration;

public interface ICalibrationStep
{
    string Name { get; }
    Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct);
}

public sealed class BaseTutorialStep(string name, TimeSpan time) : PacketHandlerAdapter, ICalibrationStep
{
    public string Name { get; } = name;
    public TimeSpan TimeToRun { get; } = time;
    private TaskCompletionSource Token = new();

    public BaseTutorialStep(string name) : this(name, TimeSpan.FromSeconds(7))
    {
    }

    public async Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.RegisterHandler(this);

        dispatcher.Dispatch(new RunVariableLenghtRoutinePacket(Name, TimeToRun));
        await WaitForRoutineFinishAsync(ct);

        dispatcher.UnRegisterHandler(this);
    }

    private async Task WaitForRoutineFinishAsync(CancellationToken ct)
    {
        await Token.Task.WaitAsync(ct);
    }

    public override void OnRoutineFinishedPacket(RoutineFinishedPacket packet)
    {
        Token.SetResult();
    }
}

public abstract class PositionalAwareCaptureStep(string name, uint flags, TimeSpan time)
    : PacketHandlerAdapter, ICalibrationStep
{
    public string Name { get; } = name;
    public uint Flags { get; } = flags;

    protected PositionalBinCollector PositionalBinCollector = new(flags);
    protected TaskCompletionSource Token = new();
    protected bool ShouldCollect = false;
    protected TimeSpan TimeToTun = time;

    public abstract Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct);

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        if (!ShouldCollect)
            return;
        PositionalBinCollector.UpdatePositionalData(positionalData);
    }

    public virtual void OnNewEyeFrame(EyePipelineEvents.NewTransformedFrameEvent frame)
    {
        if (!ShouldCollect)
            return;

        var images = frame.image.Split();
        PositionalBinCollector.AddFrame(images[1], images[0]);
    }

    protected void StartCollecting()
    {
        ShouldCollect = true;
    }

    protected void StopCollecting()
    {
        ShouldCollect = false;
    }

    protected async Task WaitForRoutineFinishAsync(CancellationToken ct)
    {
        await Token.Task.WaitAsync(ct);
    }

    public override void OnRoutineFinishedPacket(RoutineFinishedPacket packet)
    {
        Token.SetResult();
    }

    public void Dispose()
    {
        Token.SetCanceled();
    }
}

public abstract class BaseCaptureStep(string name, uint flags, TimeSpan time) : PacketHandlerAdapter, ICalibrationStep
{
    public string Name { get; } = name;
    public uint Flags { get; } = flags;

    protected BinCollector BinCollector = new(flags);
    protected TaskCompletionSource Token = new();
    protected bool ShouldCollect = false;
    protected TimeSpan TimeToTun = time;

    public abstract Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct);

    public virtual void OnNewEyeFrame(EyePipelineEvents.NewTransformedFrameEvent frame)
    {
        if (!ShouldCollect)
            return;

        var images = frame.image.Split();
        AddFrame(images);
    }

    public virtual Frame AddFrame(Mat[] images)
    {
        return BinCollector.AddFrame(images[1], images[0]);
    }

    protected void StartCollecting()
    {
        ShouldCollect = true;
    }

    protected void StopCollecting()
    {
        ShouldCollect = false;
    }

    protected async Task WaitForRoutineFinishAsync(CancellationToken ct)
    {
        await Token.Task.WaitAsync(ct);
    }

    public override void OnRoutineFinishedPacket(RoutineFinishedPacket packet)
    {
        Token.SetResult();
    }

    public void Dispose()
    {
        Token.SetCanceled();
    }
}

public class GazeCaptureStep(IEyePipelineEventBus bus, TimeSpan time, string name = "gaze", uint extraFlags = 0) : BasePositionalAwareEyeCaptureStep(bus, name,
    CaptureFlags.FLAG_GOOD_DATA |
    CaptureFlags.FLAG_IN_MOVEMENT |
    CaptureFlags.FLAG_VERSION_BIT1 |
    CaptureFlags.FLAG_ROUTINE_BIT1 | extraFlags, time)
{
    private Stopwatch _posDataTimer = new();
    private readonly TimeSpan _posDataTimeout = TimeSpan.FromSeconds(0.2);

    public GazeCaptureStep(IEyePipelineEventBus bus) : this(bus, TimeSpan.FromSeconds(60))
    {
    }

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        if (!ShouldCollect)
            return;

        PositionalBinCollector.UpdatePositionalData(positionalData);
        _posDataTimer.Restart();
    }

    public override void OnNewEyeFrame(EyePipelineEvents.NewTransformedFrameEvent frame)
    {
        if (!ShouldCollect)
            return;
        if (_posDataTimer.Elapsed <= _posDataTimeout)
        {
            var images = frame.image.Split();
            var f = PositionalBinCollector.AddFrame(images[1], images[0]);
            if (f is not null)
            {
                f.Header = f.Header with
                {
                    RoutineLeftLid = 1,
                    RoutineRightLid = 1,
                };
            }
        }
    }
}

/// <summary>
/// Records frames with BOTH per-frame gaze ground-truth (injected from the overlay reticle
/// via <see cref="PositionalBinCollector"/>) AND a held-expression label stamped onto each frame.
/// Modeled on <see cref="GazeCaptureStep"/> (same fresh-positional-data gate) but with a fully
/// parameterized expression label so the gaze dot can be recorded during the squint/widen/brow passes.
/// </summary>
public class GazeExpressionCaptureStep(
    IEyePipelineEventBus bus,
    string name,
    uint flags,
    TimeSpan time,
    float lid = 0,
    float browRaise = 0,
    float browAngry = 0,
    float widen = 0,
    float squint = 0,
    float dilate = 0)
    // FLAG_ROUTINE_BIT1 (= gaze-valid / Python FLAG_GAZE_DATA) is forced ON: every GazeExpressionCaptureStep
    // records REAL per-frame gaze from the reticle, so the gaze net trains on these held-expression+gaze
    // frames (fixes "eye looks up while squinting"). They are NOT FLAG_FREE_EXPRESSION, so they keep their
    // real expr labels for the supervised expr net (kept by exclude_expr_unlabeled, not the unlabeled stream).
    : BasePositionalAwareEyeCaptureStep(bus, name, flags | CaptureFlags.FLAG_ROUTINE_BIT1, time)
{
    private readonly Stopwatch _posDataTimer = new();
    private readonly TimeSpan _posDataTimeout = TimeSpan.FromSeconds(0.2);

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        if (!ShouldCollect)
            return;

        PositionalBinCollector.UpdatePositionalData(positionalData);
        _posDataTimer.Restart();
    }

    public override void OnNewEyeFrame(EyePipelineEvents.NewTransformedFrameEvent frame)
    {
        if (!ShouldCollect)
            return;
        if (_posDataTimer.Elapsed <= _posDataTimeout)
        {
            var images = frame.image.Split();
            var f = PositionalBinCollector.AddFrame(images[1], images[0]);
            if (f is not null)
            {
                f.Header = f.Header with
                {
                    RoutineLeftLid = lid,
                    RoutineRightLid = lid,
                    RoutineBrowRaise = browRaise,
                    RoutineBrowAngry = browAngry,
                    RoutineWiden = widen,
                    RoutineSquint = squint,
                    RoutineDilate = dilate,
                };
            }
        }
    }
}

public class BasePositionalAwareEyeCaptureStep(
    IEyePipelineEventBus eyePipelineEvent,
    string name,
    uint flags,
    TimeSpan time)
    : PositionalAwareCaptureStep(name, flags, time)
{
    public override async Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.RegisterHandler(this);

        eyePipelineEvent.Subscribe<EyePipelineEvents.NewTransformedFrameEvent>(OnNewEyeFrame);

        dispatcher.Dispatch(new RunVariableLenghtRoutinePacket(Name, TimeToTun));
        StartCollecting();
        await WaitForRoutineFinishAsync(ct);

        eyePipelineEvent.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(OnNewEyeFrame);
        dispatcher.UnRegisterHandler(this);

        if (ct.IsCancellationRequested)
            return;

        PositionalBinCollector.WriteBin(Name + ".bin");
    }
}

public class BaseEyeCaptureStep(
    IEyePipelineEventBus eyePipelineEvent,
    string name,
    uint flags,
    TimeSpan time,
    float lid = 0,
    float browRaise = 0,
    float browAngry = 0,
    float widen = 0,
    float squint = 0,
    float dilate = 0)
    : BaseCaptureStep(name, flags, time)
{
    public override async Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.RegisterHandler(this);

        eyePipelineEvent.Subscribe<EyePipelineEvents.NewTransformedFrameEvent>(OnNewEyeFrame);

        dispatcher.Dispatch(new RunVariableLenghtRoutinePacket(Name, TimeToTun));
        StartCollecting();
        await WaitForRoutineFinishAsync(ct);

        eyePipelineEvent.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(OnNewEyeFrame);
        dispatcher.UnRegisterHandler(this);

        if (ct.IsCancellationRequested)
            return;

        BinCollector.WriteBin(Name + ".bin");
    }

    public override Frame AddFrame(Mat[] images)
    {
        var frame = base.AddFrame(images);
        frame.Header = frame.Header with
        {
            RoutineLeftLid = lid,
            RoutineRightLid = lid,
            RoutineBrowRaise = browRaise,
            RoutineBrowAngry = browAngry,
            RoutineWiden = widen,
            RoutineSquint = squint,
            RoutineDilate = dilate,
        };
        return frame;
    }
}

public class CommandDispatchStep(string name) : ICalibrationStep
{
    public string Name { get; } = name;

    public Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.Dispatch(new RunFixedLenghtRoutinePacket(Name));
        return Task.CompletedTask;
    }
}

public class TrainerCalibrationStep(ITrainerService overlayTrainer) : ICalibrationStep
{
    public string Name => "trainer";

    public async Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.Dispatch(new RunVariableLenghtRoutinePacket(Name, TimeSpan.FromSeconds(120)));
        var onProgressHandler = (TrainerProgressReportPacket packet) => { dispatcher.Dispatch(packet); };
        overlayTrainer.OnProgress += onProgressHandler;
        overlayTrainer.RunTraining(Path.Combine(Utils.ModelDataDirectory, "user_cal.bin"),
            Path.Combine(Utils.ModelDataDirectory, "tuned_temporal_eye_tracking_latest.onnx"));
        await overlayTrainer.WaitAsync();

        overlayTrainer.OnProgress -= onProgressHandler;
    }
}

public class EyeCaptureStepFactory(IEyePipelineEventBus eyePipelineEvent)
{
    public BaseEyeCaptureStep Create(string name, uint flags, TimeSpan time,
        float lid = 0,
        float browRaise = 0,
        float browAngry = 0,
        float widen = 0,
        float squint = 0,
        float dilate = 0) =>
        new(eyePipelineEvent, name, flags, time, lid, browRaise, browAngry, widen, squint, dilate);

    /// <summary>
    /// Like <see cref="Create"/>, but the step also records per-frame gaze ground-truth from the
    /// overlay reticle (so the gaze dot is shown and captured during the expression pass).
    /// </summary>
    public GazeExpressionCaptureStep CreateGazeExpression(string name, uint flags, TimeSpan time,
        float lid = 0,
        float browRaise = 0,
        float browAngry = 0,
        float widen = 0,
        float squint = 0,
        float dilate = 0) =>
        new(eyePipelineEvent, name, flags, time, lid, browRaise, browAngry, widen, squint, dilate);
}

public class MergeBinsStep(params string[] binNames) : ICalibrationStep
{
    public string Name => "bin_merger";

    public Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        MergeBins("user_cal.bin", binNames);
        return Task.CompletedTask;
    }

    private static void MergeBins(string result, params string[] inputs)
    {
        var resultPath = Path.Combine(Utils.ModelDataDirectory, result);
        var inputPaths = inputs.Select(i => Path.Combine(Utils.ModelDataDirectory, i)).ToArray();
        CaptureBin.IO.CaptureBin.Concatenate(resultPath, inputPaths);
    }
}

public class EyeCalibration(
    EyeCaptureStepFactory eyeCaptureStepFactory,
    ITrainerService trainer,
    IEyePipelineEventBus eyePipelineEventBus)
{
    public IEnumerable<ICalibrationStep> BasicAllCalibration()
    {
        List<ICalibrationStep> steps =
        [
            new BaseTutorialStep("gazetutorial"),
            new GazeCaptureStep(eyePipelineEventBus),
            // Gaze-valid section with FREE/RANDOM (unlabeled) expressions: real expressions in the
            // images while gaze is labeled by the reticle. The qpro trainer uses this as its
            // unlabeled-expression section (gaze_valid=1 -> used for gaze robustness, excluded from
            // supervised expression). Same flags as the neutral gaze pass; only the name/duration differ.
            new BaseTutorialStep("gazeexprtutorial", TimeSpan.FromSeconds(5)),
            new GazeCaptureStep(eyePipelineEventBus, TimeSpan.FromSeconds(60), "gazeexpr", CaptureFlags.FLAG_FREE_EXPRESSION),
            new BaseTutorialStep("blinktutorial", TimeSpan.FromSeconds(5)),
            eyeCaptureStepFactory.Create("blink",
                CaptureFlags.FLAG_GOOD_DATA |
                CaptureFlags.FLAG_IN_MOVEMENT |
                CaptureFlags.FLAG_VERSION_BIT1,
                TimeSpan.FromSeconds(10), lid: 0
            ),

            new BaseTutorialStep("widentutorial", TimeSpan.FromSeconds(5)),
                eyeCaptureStepFactory.CreateGazeExpression("widen",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(25), widen: 1, lid: 1),

            new BaseTutorialStep("squinttutorial", TimeSpan.FromSeconds(5)),
                eyeCaptureStepFactory.CreateGazeExpression("squint",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(10), squint: 1, lid: 1),

            new BaseTutorialStep("browtutorial", TimeSpan.FromSeconds(5)),
                eyeCaptureStepFactory.CreateGazeExpression("brow",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(30), browAngry: 1, lid: 1),
            //steps.Add(new BaseTutorialStep("covergencetutorial"));
            //steps.Add(_eyeCaptureStepFactory.Create("covergence",
            //    CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_WHATEVER_NOT_IMPLEMENTED));

            new MergeBinsStep("gaze.bin", "gazeexpr.bin", "blink.bin", "widen.bin", "squint.bin", "brow.bin"),
            // new MergeBinsStep("gaze.bin", "blink.bin"),
            new TrainerCalibrationStep(trainer),
            new CommandDispatchStep("close")

        ];

        return steps;
    }

    public IEnumerable<ICalibrationStep> BasicAllCalibrationQuick()
    {
        List<ICalibrationStep> steps =
        [
            new BaseTutorialStep("gazetutorialshort", TimeSpan.FromSeconds(5)),
            new GazeCaptureStep(eyePipelineEventBus, TimeSpan.FromSeconds(10)),
            new BaseTutorialStep("gazeexprtutorial", TimeSpan.FromSeconds(4)),
            new GazeCaptureStep(eyePipelineEventBus, TimeSpan.FromSeconds(15), "gazeexpr", CaptureFlags.FLAG_FREE_EXPRESSION),
            new BaseTutorialStep("blinktutorial", TimeSpan.FromSeconds(4)),
            eyeCaptureStepFactory.Create("blink",
                CaptureFlags.FLAG_GOOD_DATA |
                CaptureFlags.FLAG_IN_MOVEMENT |
                CaptureFlags.FLAG_VERSION_BIT1 |
                CaptureFlags.FLAG_ROUTINE_BIT1,
                TimeSpan.FromSeconds(20)
            ),

            new BaseTutorialStep("widentutorial", TimeSpan.FromSeconds(4)),
                eyeCaptureStepFactory.CreateGazeExpression("widen",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(20), widen: 1, lid: 1),

            new BaseTutorialStep("squinttutorial", TimeSpan.FromSeconds(4)),
                eyeCaptureStepFactory.CreateGazeExpression("squint",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(20), squint: 1, lid: 1),

            new BaseTutorialStep("browtutorial", TimeSpan.FromSeconds(4)),
                eyeCaptureStepFactory.CreateGazeExpression("brow",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(20), browAngry: 1, lid: 1),

            new MergeBinsStep("gaze.bin", "gazeexpr.bin", "blink.bin", "widen.bin", "squint.bin", "brow.bin"),
            // new MergeBinsStep("gaze.bin", "blink.bin"),
            new TrainerCalibrationStep(trainer),
            new CommandDispatchStep("close")

        ];

        return steps;
    }

    public IEnumerable<ICalibrationStep> GazeCalibration()
    {
        List<ICalibrationStep> steps =
        [
            new BaseTutorialStep("gazetutorialshort", TimeSpan.FromSeconds(5)),
            new GazeCaptureStep(eyePipelineEventBus),

            new MergeBinsStep("gaze.bin", "blink.bin"),
            new TrainerCalibrationStep(trainer),
            new CommandDispatchStep("close")

        ];

        return steps;
    }

    public IEnumerable<ICalibrationStep> BlinkCalibration()
    {
        List<ICalibrationStep> steps =
        [
            new BaseTutorialStep("blinktutorial", TimeSpan.FromSeconds(4)),
            eyeCaptureStepFactory.Create("blink",
                CaptureFlags.FLAG_GOOD_DATA |
                CaptureFlags.FLAG_IN_MOVEMENT |
                CaptureFlags.FLAG_VERSION_BIT1 |
                CaptureFlags.FLAG_ROUTINE_BIT1,
                TimeSpan.FromSeconds(20)
            ),

            new MergeBinsStep("gaze.bin", "blink.bin"),
            new TrainerCalibrationStep(trainer),
            new CommandDispatchStep("close")

        ];

        return steps;
    }
}
