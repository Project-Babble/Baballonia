using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baballonia.CaptureBin.IO;
using Baballonia.Contracts;
using Baballonia.Desktop.Trainer;
using Baballonia.Services;
using Baballonia.Services.events;
using Google.Protobuf.WellKnownTypes;
using OpenCvSharp;
using OverlaySDK;
using OverlaySDK.Packets;

namespace Baballonia.Desktop.Calibration;

public interface ICalibrationStep
{
    string Name { get; }
    Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct);
}

public class BaseTutorialStep : PacketHandlerAdapter, ICalibrationStep
{
    public string Name { get; }
    public TimeSpan timeToRun { get; }
    protected TaskCompletionSource _token = new();

    public BaseTutorialStep(string name) : this(name, TimeSpan.FromSeconds(30))
    {
    }

    public BaseTutorialStep(string name, TimeSpan time)
    {
        Name = name;
        timeToRun = time;
    }

    public virtual async Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.RegisterHandler(this);

        dispatcher.Dispatch(new RunVariableLenghtRoutinePacket(Name, timeToRun));
        await WaitForRoutineFinishAsync(ct);

        dispatcher.UnRegisterHandler(this);
    }

    protected async Task WaitForRoutineFinishAsync(CancellationToken ct)
    {
        await _token.Task.WaitAsync(ct);
    }

    public override void OnRoutineFinishedPacket(RoutineFinishedPacket packet)
    {
        _token.SetResult();
    }
}

public abstract class PositionalAwareCaptureStep : PacketHandlerAdapter, ICalibrationStep
{
    public string Name { get; }
    public uint Flags { get; }

    protected PositionalBinCollector _positionalBinCollector;
    protected TaskCompletionSource _token = new();
    protected bool _shouldCollect = false;
    protected TimeSpan _timeToTun;

    public PositionalAwareCaptureStep(string name, uint flags, TimeSpan time)
    {
        Name = name;
        Flags = flags;
        _positionalBinCollector = new PositionalBinCollector(flags);
        _timeToTun = time;
    }

    public abstract Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct);

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        if (!_shouldCollect)
            return;
        _positionalBinCollector.UpdatePositionalData(positionalData);
    }

    public virtual void OnNewEyeFrame(EyePipelineEvents.NewTransformedFrameEvent frame)
    {
        if (!_shouldCollect)
            return;

        var images = frame.image.Split();
        _positionalBinCollector.AddFrame(images[1], images[0]);
    }

    protected void StartCollecting()
    {
        _shouldCollect = true;
    }

    protected void StopCollecting()
    {
        _shouldCollect = false;
    }

    protected async Task WaitForRoutineFinishAsync(CancellationToken ct)
    {
        await _token.Task.WaitAsync(ct);
    }

    public override void OnRoutineFinishedPacket(RoutineFinishedPacket packet)
    {
        _token.SetResult();
    }

    public void Dispose()
    {
        _token.SetCanceled();
    }
}

public abstract class BaseCaptureStep : PacketHandlerAdapter, ICalibrationStep
{
    public string Name { get; }
    public uint Flags { get; }

    protected BinCollector _binCollector;
    protected TaskCompletionSource _token = new();
    protected bool _shouldCollect = false;
    protected TimeSpan _timeToTun;

    public BaseCaptureStep(string name, uint flags, TimeSpan time)
    {
        Name = name;
        Flags = flags;
        _binCollector = new BinCollector(flags);
        _timeToTun = time;
    }

    public abstract Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct);

    public virtual void OnNewEyeFrame(EyePipelineEvents.NewTransformedFrameEvent frame)
    {
        if (!_shouldCollect)
            return;

        var images = frame.image.Split();
        AddFrame(images);
    }

    public virtual Frame AddFrame(Mat[] images)
    {
        return _binCollector.AddFrame(images[1], images[0]);
    }

    protected void StartCollecting()
    {
        _shouldCollect = true;
    }

    protected void StopCollecting()
    {
        _shouldCollect = false;
    }

    protected async Task WaitForRoutineFinishAsync(CancellationToken ct)
    {
        await _token.Task.WaitAsync(ct);
    }

    public override void OnRoutineFinishedPacket(RoutineFinishedPacket packet)
    {
        _token.SetResult();
    }

    public void Dispose()
    {
        _token.SetCanceled();
    }
}

public class GazeCaptureStep : BasePositionalAwareEyeCaptureStep
{
    private Stopwatch _posDataTimer = new();
    private readonly TimeSpan _posDataTimeout = TimeSpan.FromSeconds(0.2);

    public GazeCaptureStep(IEyePipelineEventBus bus) : this(bus, TimeSpan.FromSeconds(120))
    {
    }

    public GazeCaptureStep(IEyePipelineEventBus bus, TimeSpan time) : base(bus, "gaze",
        CaptureFlags.FLAG_GOOD_DATA |
        CaptureFlags.FLAG_IN_MOVEMENT |
        CaptureFlags.FLAG_VERSION_BIT1 |
        CaptureFlags.FLAG_ROUTINE_BIT1, time)
    {
    }

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        if (!_shouldCollect)
            return;

        _positionalBinCollector.UpdatePositionalData(positionalData);
        _posDataTimer.Restart();
    }

    public override void OnNewEyeFrame(EyePipelineEvents.NewTransformedFrameEvent frame)
    {
        if (!_shouldCollect)
            return;
        if (_posDataTimer.Elapsed <= _posDataTimeout)
        {
            var images = frame.image.Split();
            var f = _positionalBinCollector.AddFrame(images[1], images[0]);
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

        dispatcher.Dispatch(new RunVariableLenghtRoutinePacket(Name, _timeToTun));
        StartCollecting();
        await WaitForRoutineFinishAsync(ct);

        eyePipelineEvent.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(OnNewEyeFrame);
        dispatcher.UnRegisterHandler(this);

        if (ct.IsCancellationRequested)
            return;

        _positionalBinCollector.WriteBin(Name + ".bin");
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

        dispatcher.Dispatch(new RunVariableLenghtRoutinePacket(Name, _timeToTun));
        StartCollecting();
        await WaitForRoutineFinishAsync(ct);

        eyePipelineEvent.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(OnNewEyeFrame);
        dispatcher.UnRegisterHandler(this);

        if (ct.IsCancellationRequested)
            return;

        _binCollector.WriteBin(Name + ".bin");
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

public class CommandDispatchStep : ICalibrationStep
{
    public string Name { get; }

    public CommandDispatchStep(string name)
    {
        Name = name;
    }

    public Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.Dispatch(new RunFixedLenghtRoutinePacket(Name));
        return Task.CompletedTask;
    }
}

public class TrainerCalibrationStep : ICalibrationStep
{
    public string Name { get; }
    private readonly ITrainerService _trainer;

    public TrainerCalibrationStep(ITrainerService overlayTrainer)
    {
        _trainer = overlayTrainer;
        Name = "trainer";
    }

    public async Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.Dispatch(new RunVariableLenghtRoutinePacket(Name, TimeSpan.FromSeconds(120)));
        var onProgresHandler = (TrainerProgressReportPacket packet) => { dispatcher.Dispatch(packet); };
        _trainer.OnProgress += onProgresHandler;
        _trainer.RunTraining(Path.Combine(Utils.ModelDataDirectory, "user_cal.bin"),
            Path.Combine(Utils.ModelDataDirectory, "tuned_temporal_eye_tracking_latest.onnx"));
        await _trainer.WaitAsync();

        _trainer.OnProgress -= onProgresHandler;
    }
}

public class EyeCaptureStepFactory
{
    private readonly IEyePipelineEventBus _eyePipelineEvent;

    public EyeCaptureStepFactory(IEyePipelineEventBus eyePipelineEvent)
    {
        _eyePipelineEvent = eyePipelineEvent;
    }

    public BaseEyeCaptureStep Create(string name, uint flags, TimeSpan time,
        float lid = 0,
        float browRaise = 0,
        float browAngry = 0,
        float widen = 0,
        float squint = 0,
        float dilate = 0) =>
        new(_eyePipelineEvent, name, flags, time, lid, browRaise, browAngry, widen, squint, dilate);
}

public class MergeBinsStep : ICalibrationStep
{
    public string Name { get; } = "bin_merger";
    private string[] _binNames;

    public MergeBinsStep(params string[] binNames)
    {
        _binNames = binNames;
    }

    public Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        MergeBins("user_cal.bin", _binNames);
        return Task.CompletedTask;
    }

    private static void MergeBins(string result, params string[] inputs)
    {
        var resultPath = Path.Combine(Utils.ModelDataDirectory, result);
        var inputPaths = inputs.Select(i => Path.Combine(Utils.ModelDataDirectory, i)).ToArray();
        CaptureBin.IO.CaptureBin.Concatenate(resultPath, inputPaths);
    }
}

public class EyeCalibration
{
    private readonly EyeCaptureStepFactory _eyeCaptureStepFactory;
    private readonly ITrainerService _trainer;
    private readonly IEyePipelineEventBus _eyePipelineEventBus;

    public EyeCalibration(EyeCaptureStepFactory eyeCaptureStepFactory, ITrainerService trainer,
        IEyePipelineEventBus eyePipelineEventBus)
    {
        _eyeCaptureStepFactory = eyeCaptureStepFactory;
        _trainer = trainer;
        _eyePipelineEventBus = eyePipelineEventBus;
    }

    public IEnumerable<ICalibrationStep> BasicAllCalibration()
    {
        List<ICalibrationStep> steps =
        [
            new BaseTutorialStep("gazetutorial"),
            new GazeCaptureStep(_eyePipelineEventBus),
            new BaseTutorialStep("blinktutorial", TimeSpan.FromSeconds(10)),
            _eyeCaptureStepFactory.Create("blink",
                CaptureFlags.FLAG_GOOD_DATA |
                CaptureFlags.FLAG_IN_MOVEMENT |
                CaptureFlags.FLAG_VERSION_BIT1,
                TimeSpan.FromSeconds(20), lid: 0
            ),
            // steps.Add(new BaseTutorialStep("dilationtutorial"));
            // steps.Add(_eyeCaptureStepFactory.Create("dilation",
            //     CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_DILATION_BLACK));

            new BaseTutorialStep("widentutorial", TimeSpan.FromSeconds(10)),
            _eyeCaptureStepFactory.Create("widen",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(20), widen: 1, lid: 1),

            new BaseTutorialStep("squinttutorial", TimeSpan.FromSeconds(10)),
            _eyeCaptureStepFactory.Create("squint",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(20), squint: 1, lid: 1),

            new BaseTutorialStep("browtutorial", TimeSpan.FromSeconds(10)),
            _eyeCaptureStepFactory.Create("brow",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(20), browAngry: 1, lid: 1),
            // steps.Add(new BaseTutorialStep("covergencetutorial"));
            // steps.Add(_eyeCaptureStepFactory.Create("covergence",
            //     CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_WHATEVER_NOT_IMPLEMENTED));

            new MergeBinsStep("gaze.bin", "blink.bin", "widen.bin", "squint.bin", "brow.bin"),
            new TrainerCalibrationStep(_trainer),
            new CommandDispatchStep("close")

        ];
        // steps.Add(new BaseTutorialStep("dilationtutorial"));
        // steps.Add(_eyeCaptureStepFactory.Create("dilation",
        //     CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_DILATION_BLACK));
        // steps.Add(new BaseTutorialStep("covergencetutorial"));
        // steps.Add(_eyeCaptureStepFactory.Create("covergence",
        //     CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_WHATEVER_NOT_IMPLEMENTED));

        return steps;
    }
    public IEnumerable<ICalibrationStep> BasicAllCalibrationQuick()
    {
        List<ICalibrationStep> steps =
        [
            new BaseTutorialStep("gazetutorialshort", TimeSpan.FromSeconds(5)),
            new GazeCaptureStep(_eyePipelineEventBus, TimeSpan.FromSeconds(10)),
            new BaseTutorialStep("blinktutorial", TimeSpan.FromSeconds(4)),
            _eyeCaptureStepFactory.Create("blink",
                CaptureFlags.FLAG_GOOD_DATA |
                CaptureFlags.FLAG_IN_MOVEMENT |
                CaptureFlags.FLAG_VERSION_BIT1 |
                CaptureFlags.FLAG_ROUTINE_BIT1,
                TimeSpan.FromSeconds(20)
            ),

            new BaseTutorialStep("widentutorial", TimeSpan.FromSeconds(4)),
            _eyeCaptureStepFactory.Create("widen",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(20)),

            new BaseTutorialStep("squinttutorial", TimeSpan.FromSeconds(4)),
            _eyeCaptureStepFactory.Create("squint",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(20)),

            new BaseTutorialStep("browtutorial", TimeSpan.FromSeconds(4)),
            _eyeCaptureStepFactory.Create("brow",
                CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_VERSION_BIT1, TimeSpan.FromSeconds(20)),

            new MergeBinsStep("gaze.bin", "blink.bin", "widen.bin", "squint.bin", "brow.bin"),
            new TrainerCalibrationStep(_trainer),
            new CommandDispatchStep("close")

        ];

        return steps;
    }
}
