using Avalonia.Threading;
using Baballonia.Services.events;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.VideoSources;
using Microsoft.Extensions.Logging;
using System;

namespace Baballonia.Services;

public class ProcessingLoopService : IDisposable
{
    public record struct Expressions(OrderedFloatMap? FaceExpression, OrderedFloatMap? EyeExpression);

    public event Action<Expressions> ExpressionChangeEvent;

    private readonly ILogger<ProcessingLoopService> _logger;
    private readonly FaceProcessingPipeline _faceProcessingPipeline;
    private readonly FacePipelineManager _facePipelineManager;
    private readonly IFacePipelineEventBus _facePipelineEventBus;
    private readonly EyeProcessingPipeline _eyeProcessingPipeline;
    private readonly EyePipelineManager _eyePipelineManager;
    private readonly IEyePipelineEventBus _eyePipelineEventBus;
    private readonly PipelineMetrics _metrics;
    private readonly ThreadProfiler _profiler;

    // Stock expr-dev runs both pipelines sequentially on the UI thread via this 10 ms timer.
    private readonly DispatcherTimer _drawTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(10)
    };

    // Separate UI-thread heartbeat for the Debug page. Because inference runs on the UI thread here,
    // this tick rate drops when the draw timer saturates the thread — which is exactly the signal we
    // want to compare against the optimized branch (where inference is off the UI thread).
    private readonly DispatcherTimer _uiHeartbeat = new()
    {
        Interval = TimeSpan.FromMilliseconds(10)
    };
    private bool _uiRegistered;

    public ProcessingLoopService(
        ILogger<ProcessingLoopService> logger,
        EyeProcessingPipeline eyeProcessingPipeline, FaceProcessingPipeline faceProcessingPipeline,
        IFacePipelineEventBus facePipelineEventBus, IEyePipelineEventBus eyePipelineEventBus,
        FacePipelineManager facePipelineManager, EyePipelineManager eyePipelineManager,
        PipelineMetrics metrics, ThreadProfiler profiler)
    {
        _logger = logger;
        _eyeProcessingPipeline = eyeProcessingPipeline;
        _faceProcessingPipeline = faceProcessingPipeline;
        _facePipelineEventBus = facePipelineEventBus;
        _eyePipelineEventBus = eyePipelineEventBus;
        _facePipelineManager = facePipelineManager;
        _eyePipelineManager = eyePipelineManager;
        _metrics = metrics;
        _profiler = profiler;

        _uiHeartbeat.Tick += (_, _) =>
        {
            // First tick runs on the UI thread, so it captures the UI thread's OS id for the profiler.
            if (!_uiRegistered)
            {
                _profiler.Register("UI");
                _uiRegistered = true;
            }
            _metrics.UiTicks++;
        };
        _uiHeartbeat.Start();

        _drawTimer.Tick += TimerEvent;
        _drawTimer.Start();
    }

    private void TimerEvent(object? s, EventArgs e)
    {
        var expressions = new Expressions();

        try
        {
            var faceExpression = _faceProcessingPipeline.RunUpdate();
            if (faceExpression != null)
            {
                expressions.FaceExpression = faceExpression;
                _metrics.FaceInferences++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected exception in Face Tracking pipeline, stopping... : {}", ex);
            _facePipelineManager.StopCamera();
            _facePipelineEventBus.Publish(new FacePipelineEvents.ExceptionEvent(ex));
        }

        try
        {
            var eyeExpression = _eyeProcessingPipeline.RunUpdate();
            if (eyeExpression != null)
            {
                expressions.EyeExpression = eyeExpression;
                _metrics.EyeInferences++;
                UpdateEyeCameraMetrics();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected exception in Eye Tracking pipeline, stopping... : {}", ex);
            _eyePipelineManager.StopAllCameras();
            _eyePipelineEventBus.Publish(new EyePipelineEvents.ExceptionEvent(ex));
        }

        if (expressions.FaceExpression != null || expressions.EyeExpression != null)
            ExpressionChangeEvent?.Invoke(expressions);
    }

    /// <summary>Snapshot the eye camera's resolution into <see cref="PipelineMetrics"/> for the Debug page.
    /// Stock expr-dev's capture doesn't expose delivered-FPS/format, so only the resolution is populated.</summary>
    private void UpdateEyeCameraMetrics()
    {
        var source = _eyeProcessingPipeline.VideoSource;
        var sized = source as SingleCameraSource
                    ?? (source as DualCameraSource)?.LeftCam as SingleCameraSource
                    ?? (source as DualCameraSource)?.RightCam as SingleCameraSource;
        if (sized != null)
        {
            _metrics.EyeCameraWidth = sized.CameraSize.Width;
            _metrics.EyeCameraHeight = sized.CameraSize.Height;
        }
    }

    public void Start()
    {
        _drawTimer.Start();
    }

    public void Pause()
    {
        _drawTimer.Stop();
    }

    public void Dispose()
    {
        _uiHeartbeat.Stop();
        _drawTimer.Stop();
        _faceProcessingPipeline.VideoSource?.Dispose();
        _eyeProcessingPipeline.VideoSource?.Dispose();
    }
}
