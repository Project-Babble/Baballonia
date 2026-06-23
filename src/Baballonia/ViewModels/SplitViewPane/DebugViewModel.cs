using Avalonia.Threading;
using Baballonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace Baballonia.ViewModels.SplitViewPane;

/// <summary>One row in the per-thread CPU table.</summary>
public partial class ThreadCpuRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _cpuPercent;
}

/// <summary>
/// Live performance/diagnostics page: samples the monotonic <see cref="PipelineMetrics"/> counters
/// twice a second and derives rates (UI loop, eye/face inference, camera throughput), and surfaces the
/// per-thread CPU snapshot from the always-on <see cref="ThreadProfiler"/> plus per-stage pipeline timings.
/// The profiler samples continuously on its own background thread, so the hotspot data reflects the whole
/// app — not just whatever happens while this page is on screen.
/// </summary>
public partial class DebugViewModel : ViewModelBase, IDisposable
{
    private const int MaxThreadRows = 14;

    private readonly PipelineMetrics _metrics;
    private readonly ThreadProfiler _profiler;
    private readonly DispatcherTimer _timer;

    private long _prevUi, _prevEye, _prevFace, _prevCam;
    private DateTime _prevTime;

    [ObservableProperty] private double _uiLoopFps;
    [ObservableProperty] private double _eyeInferenceFps;
    [ObservableProperty] private double _faceInferenceFps;
    [ObservableProperty] private double _cameraFps;
    [ObservableProperty] private double _cameraTargetFps;
    [ObservableProperty] private string _cameraResolution = "—";
    [ObservableProperty] private string _cameraFormat = "—";

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
        _prevTime = DateTime.UtcNow;
        _prevUi = metrics.UiTicks;
        _prevEye = metrics.EyeInferences;
        _prevFace = metrics.FaceInferences;
        _prevCam = metrics.EyeCameraFrames;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += Sample;
        _timer.Start();
    }

    private void Sample(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _prevTime).TotalSeconds;
        if (dt <= 0)
            return;

        UiLoopFps = (_metrics.UiTicks - _prevUi) / dt;
        EyeInferenceFps = (_metrics.EyeInferences - _prevEye) / dt;
        FaceInferenceFps = (_metrics.FaceInferences - _prevFace) / dt;
        CameraFps = (_metrics.EyeCameraFrames - _prevCam) / dt;

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

        _prevUi = _metrics.UiTicks;
        _prevEye = _metrics.EyeInferences;
        _prevFace = _metrics.FaceInferences;
        _prevCam = _metrics.EyeCameraFrames;
        _prevTime = now;
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
