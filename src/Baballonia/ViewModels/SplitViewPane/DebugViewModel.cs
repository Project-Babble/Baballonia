using Avalonia.Threading;
using Baballonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Baballonia.ViewModels.SplitViewPane;

/// <summary>One row in the per-thread CPU table.</summary>
public partial class ThreadCpuRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _cpuPercent;
}

/// <summary>
/// Live performance page. Rates are monotonic counter deltas measured against a Stopwatch clock and
/// EWMA-smoothed; per-thread CPU comes from the always-on <see cref="ThreadProfiler"/>.
/// </summary>
public partial class DebugViewModel : ViewModelBase, IDisposable
{
    private const int MaxThreadRows = 14;

    // EWMA weight per 500 ms sample: smooths jitter but still reacts within ~1-2 s.
    private const double Smoothing = 0.3;

    private static readonly long DropWindowTicks = 60 * Stopwatch.Frequency;
    private readonly Queue<(long Timestamp, long Dropped)> _dropWindow = new();

    private readonly PipelineMetrics _metrics;
    private readonly ThreadProfiler _profiler;
    private readonly DispatcherTimer _timer;

    private long _prevUi, _prevEye, _prevFace, _prevCam, _prevRender;
    private long _prevTimestamp;

    // Incremented once per compositor frame by the view; surfaces Avalonia's render-thread fps.
    public long RenderTicks;

    [ObservableProperty] private double _renderFps;
    [ObservableProperty] private double _uiLoopFps;
    [ObservableProperty] private double _eyeInferenceFps;
    [ObservableProperty] private double _faceInferenceFps;
    [ObservableProperty] private double _cameraFps;
    [ObservableProperty] private double _cameraTargetFps;
    [ObservableProperty] private string _cameraResolution = "—";
    [ObservableProperty] private string _cameraFormat = "—";

    // Frames the camera delivered but inference skipped (delivered - inferred).
    [ObservableProperty] private string _droppedSummary = "—";

    // CPU hotspots.
    [ObservableProperty] private double _processCpuPercent;
    [ObservableProperty] private int _processorCount;
    public ObservableCollection<ThreadCpuRow> Threads { get; } = new();

    // Eye pipeline stage timings (ms).
    [ObservableProperty] private double _eyeCaptureMs;
    [ObservableProperty] private double _eyeTransformMs;
    [ObservableProperty] private double _eyeInferenceMs;
    [ObservableProperty] private double _eyePostMs;

    // Face pipeline stage timings (ms).
    [ObservableProperty] private double _faceCaptureMs;
    [ObservableProperty] private double _faceTransformMs;
    [ObservableProperty] private double _faceInferenceMs;
    [ObservableProperty] private double _facePostMs;

    public DebugViewModel(PipelineMetrics metrics, ThreadProfiler profiler)
    {
        _metrics = metrics;
        _profiler = profiler;
        ProcessorCount = profiler.ProcessorCount;
        _prevTimestamp = Stopwatch.GetTimestamp();
        _prevUi = metrics.UiTicks;
        _prevEye = metrics.EyeInferences;
        _prevFace = metrics.FaceInferences;
        _prevCam = metrics.EyeCapture?.FramesProduced ?? 0;
        _prevRender = RenderTicks;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += Sample;
        _timer.Start();
    }

    /// <summary>EWMA that seeds from the first sample (so it converges immediately, not from zero).</summary>
    private static double Smooth(double previous, double sample) =>
        previous <= 0 ? sample : previous + Smoothing * (sample - previous);

    private void Sample(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (now - _prevTimestamp) / (double)Stopwatch.Frequency;
        if (dt <= 0)
            return;

        var cam = _metrics.EyeCapture?.FramesProduced ?? 0;
        var eye = _metrics.EyeInferences;
        // Seed the baseline the first time we see a running camera, to avoid a startup spike.
        if (_prevCam == 0 && cam > 0)
            _prevCam = cam;

        RenderFps = Smooth(RenderFps, (RenderTicks - _prevRender) / dt);
        UiLoopFps = Smooth(UiLoopFps, (_metrics.UiTicks - _prevUi) / dt);
        EyeInferenceFps = Smooth(EyeInferenceFps, (eye - _prevEye) / dt);
        FaceInferenceFps = Smooth(FaceInferenceFps, (_metrics.FaceInferences - _prevFace) / dt);
        CameraFps = cam > 0 ? Smooth(CameraFps, (cam - _prevCam) / dt) : 0;

        // Frames the capture overwrote before the pipeline acquired them, over a rolling 60 s window.
        var drop = _metrics.EyeCapture?.FramesDropped ?? 0;
        _dropWindow.Enqueue((now, drop));
        while (_dropWindow.Count > 1 && _dropWindow.Peek().Timestamp < now - DropWindowTicks)
            _dropWindow.Dequeue();
        var lost = Math.Max(0, drop - _dropWindow.Peek().Dropped);
        DroppedSummary = cam > 0 ? $"{lost} frames" : "—";

        CameraTargetFps = _metrics.EyeCameraTargetFps;
        CameraResolution = _metrics.EyeCameraWidth > 0
            ? $"{_metrics.EyeCameraWidth} x {_metrics.EyeCameraHeight}"
            : "—";
        CameraFormat = string.IsNullOrEmpty(_metrics.EyeCameraFormat) ? "—" : _metrics.EyeCameraFormat;

        EyeCaptureMs = _metrics.EyeCaptureMs;
        EyeTransformMs = _metrics.EyeTransformMs;
        EyeInferenceMs = _metrics.EyeInferenceMs;
        EyePostMs = _metrics.EyePostMs;

        FaceCaptureMs = _metrics.FaceCaptureMs;
        FaceTransformMs = _metrics.FaceTransformMs;
        FaceInferenceMs = _metrics.FaceInferenceMs;
        FacePostMs = _metrics.FacePostMs;

        UpdateThreads();

        _prevRender = RenderTicks;
        _prevUi = _metrics.UiTicks;
        _prevEye = _metrics.EyeInferences;
        _prevFace = _metrics.FaceInferences;
        _prevCam = cam;
        _prevTimestamp = now;
    }

    /// <summary>Reconcile the hottest threads into the bound collection, reusing rows to avoid UI churn.</summary>
    private void UpdateThreads()
    {
        ProcessCpuPercent = _profiler.ProcessCpuPercent;

        var samples = _profiler.Snapshot;
        var show = Math.Min(samples.Count, MaxThreadRows);

        while (Threads.Count < show) Threads.Add(new ThreadCpuRow());
        while (Threads.Count > show) Threads.RemoveAt(Threads.Count - 1);

        for (var i = 0; i < show; i++)
        {
            var s = samples[i];
            Threads[i].Name = s.Count > 1 ? $"{s.Name} (×{s.Count})" : s.Name;
            Threads[i].CpuPercent = s.CpuPercent;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Sample;
    }
}
