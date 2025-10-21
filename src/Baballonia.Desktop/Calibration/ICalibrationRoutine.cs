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
    protected TaskCompletionSource _token = new();

    public BaseTutorialStep(string name)
    {
        Name = name;
    }

    public virtual async Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.RegisterHandler(this);

        dispatcher.Dispatch(new RunFixedLenghtRoutinePacket(Name));
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

public abstract class BaseCaptureStep : PacketHandlerAdapter, ICalibrationStep
{
    public string Name { get; }
    public uint Flags { get; }

    protected BinCollector _binCollector;
    protected TaskCompletionSource _token = new();
    protected bool _shouldCollect = false;

    public BaseCaptureStep(string name, uint flags)
    {
        Name = name;
        Flags = flags;
        _binCollector = new BinCollector(flags);
    }

    public abstract Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct);

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        if (!_shouldCollect)
            return;
        _binCollector.UpdatePositionalData(positionalData);
    }

    public virtual void OnNewEyeFrame(EyePipelineEvents.NewTransformedFrameEvent frame)
    {
        if (!_shouldCollect)
            return;

        var images = frame.image.Split();
        _binCollector.AddFrame(images[0], images[1]);
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

public class GazeCaptureStep : BaseEyeCaptureStep
{
    private Stopwatch _posDataTimer = new();
    private readonly TimeSpan _posDataTimeout = TimeSpan.FromSeconds(0.2);
    public GazeCaptureStep(IEyePipelineEventBus bus) : base(bus, "gaze", CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_IN_MOVEMENT)
    {
    }

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        if (!_shouldCollect)
            return;

        _binCollector.UpdatePositionalData(positionalData);
        _posDataTimer.Restart();
    }

    public override void OnNewEyeFrame(EyePipelineEvents.NewTransformedFrameEvent frame)
    {
        if (!_shouldCollect)
            return;
        if (_posDataTimer.Elapsed <= _posDataTimeout)
        {
            var images = frame.image.Split();
            _binCollector.AddFrame(images[0], images[1]);
        }
    }
}

public class BaseEyeCaptureStep : BaseCaptureStep
{
    private readonly IEyePipelineEventBus _eyePipelineEvent;

    public BaseEyeCaptureStep(IEyePipelineEventBus eyePipelineEvent, string name, uint flags) : base(name, flags)
    {
        _eyePipelineEvent = eyePipelineEvent;
    }

    public override async Task ExecuteAsync(OverlayMessageDispatcher dispatcher, CancellationToken ct)
    {
        dispatcher.RegisterHandler(this);

        _eyePipelineEvent.Subscribe<EyePipelineEvents.NewTransformedFrameEvent>(OnNewEyeFrame);

        dispatcher.Dispatch(new RunVariableLenghtRoutinePacket(Name, TimeSpan.FromSeconds(120)));
        StartCollecting();
        await WaitForRoutineFinishAsync(ct);
        if (ct.IsCancellationRequested)
            return;

        _eyePipelineEvent.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(OnNewEyeFrame);
        dispatcher.UnRegisterHandler(this);
        _binCollector.WriteBin(Name + ".bin");
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
        var onProgresHandler = (TrainerProgressReportPacket packet) =>
        {
            dispatcher.Dispatch(packet);
        };
        _trainer.OnProgress += onProgresHandler;
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

    public BaseEyeCaptureStep Create(string name, uint flags)
    {
        return new BaseEyeCaptureStep(_eyePipelineEvent, name, flags);
    }
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
    void MergeBins(string result, params string[] inputs)
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

    public EyeCalibration(EyeCaptureStepFactory eyeCaptureStepFactory, ITrainerService trainer, IEyePipelineEventBus eyePipelineEventBus)
    {
        _eyeCaptureStepFactory = eyeCaptureStepFactory;
        _trainer = trainer;
        _eyePipelineEventBus = eyePipelineEventBus;
    }

    public IEnumerable<ICalibrationStep> BasicAllCalibration()
    {
        List<ICalibrationStep> steps = [];
        steps.Add(new BaseTutorialStep("gazetutorial"));
        steps.Add(new GazeCaptureStep(_eyePipelineEventBus));
        steps.Add(new BaseTutorialStep("blinktutorial"));
        steps.Add(_eyeCaptureStepFactory.Create("blink", CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_RESTING));
        // steps.Add(new BaseTutorialStep("dilationtutorial"));
        // steps.Add(_eyeCaptureStepFactory.Create("dilation",
        //     CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_DILATION_BLACK));
        // steps.Add(new BaseTutorialStep("widentutorial"));
        // steps.Add(_eyeCaptureStepFactory.Create("widen",
        //     CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_WHATEVER_NOT_IMPLEMENTED));
        // steps.Add(new BaseTutorialStep("squinttutorial"));
        // steps.Add(_eyeCaptureStepFactory.Create("squint",
        //     CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_WHATEVER_NOT_IMPLEMENTED));
        // steps.Add(new BaseTutorialStep("browtutorial"));
        // steps.Add(_eyeCaptureStepFactory.Create("brow",
        //     CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_WHATEVER_NOT_IMPLEMENTED));
        // steps.Add(new BaseTutorialStep("covergencetutorial"));
        // steps.Add(_eyeCaptureStepFactory.Create("covergence",
        //     CaptureFlags.FLAG_GOOD_DATA | CaptureFlags.FLAG_WHATEVER_NOT_IMPLEMENTED));
        steps.Add(new MergeBinsStep("gaze.bin", "blink.bin", "dilation.bin"));
        steps.Add(new TrainerCalibrationStep(_trainer));
        steps.Add(new CommandDispatchStep("close"));

        return steps;
    }
}
