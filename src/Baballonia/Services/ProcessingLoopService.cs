using Avalonia.Threading;
using Baballonia.SDK;
using Baballonia.Services.events;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.VideoSources;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace Baballonia.Services;

/// <summary>
/// Drives the eye and face inference pipelines. Each pipeline runs on its own dedicated background
/// thread, decoupled from the UI thread <em>and</em> from each other: a worker blocks until its
/// camera delivers a fresh frame (via the capture's frame signal) and then runs one inference pass.
/// This lets eye inference run at the camera's full frame-rate instead of the old UI-timer cadence,
/// and stops face inference latency from throttling the eye loop.
/// </summary>
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

    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _eyeThread;
    private readonly Thread _faceThread;
    private volatile bool _paused;
    private bool _uiRegistered;

    // Upper bound on how long a worker blocks waiting for a frame before looping again, so it can
    // still notice pause/cancel and re-sample a camera that has just started producing frames.
    private const int FrameWaitTimeoutMs = 50;

    // A lightweight UI-thread heartbeat. Now that inference no longer runs on the UI thread, this
    // simply measures UI-thread responsiveness for the Debug page (it stalls if the UI is saturated).
    private readonly DispatcherTimer _uiHeartbeat = new()
    {
        Interval = TimeSpan.FromMilliseconds(10)
    };

    public ProcessingLoopService(
        ILogger<ProcessingLoopService> logger,
        EyeProcessingPipeline eyeProcessingPipeline, FaceProcessingPipeline faceProcessingPipeline,
        IFacePipelineEventBus facePipelineEventBus, IEyePipelineEventBus eyePipelineEventBus,
        FacePipelineManager facePipelineManager, EyePipelineManager eyePipelineManager,
        PipelineMetrics metrics, ThreadProfiler profiler)
    {
        _logger = logger;
        _metrics = metrics;
        _profiler = profiler;
        _eyeProcessingPipeline = eyeProcessingPipeline;
        _faceProcessingPipeline = faceProcessingPipeline;
        _facePipelineEventBus = facePipelineEventBus;
        _eyePipelineEventBus = eyePipelineEventBus;
        _facePipelineManager = facePipelineManager;
        _eyePipelineManager = eyePipelineManager;

        _uiHeartbeat.Tick += (_, _) =>
        {
            // The heartbeat fires on the UI thread, so the first tick captures its OS id for the profiler.
            if (!_uiRegistered)
            {
                _profiler.Register("UI");
                _uiRegistered = true;
            }
            _metrics.UiTicks++;
        };
        _uiHeartbeat.Start();

        _eyeThread = new Thread(EyeWorker) { IsBackground = true, Name = "EyeInference" };
        _faceThread = new Thread(FaceWorker) { IsBackground = true, Name = "MouthInference" };
        _eyeThread.Start();
        _faceThread.Start();
    }

    private void EyeWorker()
    {
        _profiler.Register("EyeInference");
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                WaitForFrame(_eyeProcessingPipeline, ct);
                if (ct.IsCancellationRequested) break;
                if (_paused) continue;

                OrderedFloatMap? eyeExpression;
                lock (_eyeProcessingPipeline.SyncRoot)
                {
                    eyeExpression = _eyeProcessingPipeline.RunUpdate();
                    UpdateEyeCameraMetrics();
                }

                if (eyeExpression == null)
                    continue;

                Interlocked.Increment(ref _metrics.EyeInferences);
                ExpressionChangeEvent?.Invoke(new Expressions(null, eyeExpression));
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected exception in Eye Tracking pipeline, stopping... : {}", ex);
                _eyePipelineManager.StopAllCameras();
                _eyePipelineEventBus.Publish(new EyePipelineEvents.ExceptionEvent(ex));
            }
        }
    }

    private void FaceWorker()
    {
        _profiler.Register("MouthInference");
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                WaitForFrame(_faceProcessingPipeline, ct);
                if (ct.IsCancellationRequested) break;
                if (_paused) continue;

                OrderedFloatMap? faceExpression;
                lock (_faceProcessingPipeline.SyncRoot)
                {
                    faceExpression = _faceProcessingPipeline.RunUpdate();
                }

                if (faceExpression == null)
                    continue;

                Interlocked.Increment(ref _metrics.FaceInferences);
                ExpressionChangeEvent?.Invoke(new Expressions(faceExpression, null));
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected exception in Face Tracking pipeline, stopping... : {}", ex);
                _facePipelineManager.StopCamera();
                _facePipelineEventBus.Publish(new FacePipelineEvents.ExceptionEvent(ex));
            }
        }
    }

    /// <summary>
    /// Block until the pipeline's camera(s) deliver a fresh frame, or <see cref="FrameWaitTimeoutMs"/>
    /// elapses, or cancellation is requested. Falls back to a plain (cancellation-aware) sleep when no
    /// camera is active or the source is being torn down concurrently.
    /// </summary>
    private static void WaitForFrame(DefaultProcessingPipeline pipeline, CancellationToken ct)
    {
        WaitHandle[] handles;
        try
        {
            var sourceHandles = pipeline.VideoSource?.GetFrameWaitHandles() ?? Array.Empty<WaitHandle>();
            if (sourceHandles.Length == 0)
            {
                ct.WaitHandle.WaitOne(FrameWaitTimeoutMs);
                return;
            }

            handles = new WaitHandle[sourceHandles.Length + 1];
            Array.Copy(sourceHandles, handles, sourceHandles.Length);
            handles[^1] = ct.WaitHandle;
        }
        catch
        {
            // Source was swapped/disposed mid-read; just pace ourselves and retry next loop.
            ct.WaitHandle.WaitOne(FrameWaitTimeoutMs);
            return;
        }

        try
        {
            WaitHandle.WaitAny(handles, FrameWaitTimeoutMs);
        }
        catch
        {
            ct.WaitHandle.WaitOne(FrameWaitTimeoutMs);
        }
    }

    /// <summary>Snapshot the eye camera's throughput/stats into <see cref="PipelineMetrics"/>.
    /// Called while holding the eye pipeline's <see cref="DefaultProcessingPipeline.SyncRoot"/>.</summary>
    private void UpdateEyeCameraMetrics()
    {
        var source = _eyeProcessingPipeline.VideoSource;
        Capture? capture = source switch
        {
            SingleCameraSource s => s.Capture,
            DualCameraSource d => (d.LeftCam as SingleCameraSource)?.Capture
                                  ?? (d.RightCam as SingleCameraSource)?.Capture,
            _ => null
        };
        if (capture == null)
            return;

        _metrics.EyeCameraFrames = capture.FramesProduced;
        _metrics.EyeCameraTargetFps = capture.TargetFps;
        _metrics.EyeCameraFormat = capture.PixelFormatName;

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
        _paused = false;
    }

    public void Pause()
    {
        _paused = true;
    }

    public void Dispose()
    {
        _uiHeartbeat.Stop();
        _cts.Cancel();
        if (_eyeThread.IsAlive) _eyeThread.Join(TimeSpan.FromSeconds(2));
        if (_faceThread.IsAlive) _faceThread.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _faceProcessingPipeline.VideoSource?.Dispose();
        _eyeProcessingPipeline.VideoSource?.Dispose();
    }
}
