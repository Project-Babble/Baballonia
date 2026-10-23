using Avalonia.Threading;
using Baballonia.Services;
using Baballonia.Services.Inference.VideoSources;
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

/// <summary>One camera card (eye left/right or mouth).</summary>
public partial class CameraRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _deliveredFps;
    [ObservableProperty] private string _droppedSummary = "—";
    [ObservableProperty] private double _negotiatedFps;
    [ObservableProperty] private string _resolution = "—";
    [ObservableProperty] private string _format = "—";
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

    private sealed class CamState
    {
        public long PrevFrames;
        public double FpsEwma;
        public readonly Queue<(long Timestamp, long Dropped)> DropWindow = new();
    }

    private readonly PipelineMetrics _metrics;
    private readonly ThreadProfiler _profiler;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<SingleCameraSource, CamState> _camStates = new();

    private long _prevUi, _prevEye, _prevFace, _prevRender;
    private long _prevTimestamp;

    // Incremented once per compositor frame by the view; surfaces Avalonia's render-thread fps.
    public long RenderTicks;

    [ObservableProperty] private double _renderFps;
    [ObservableProperty] private double _uiLoopFps;
    [ObservableProperty] private double _eyeInferenceFps;
    [ObservableProperty] private double _faceInferenceFps;

    // CPU hotspots.
    [ObservableProperty] private double _processCpuPercent;
    [ObservableProperty] private int _processorCount;
    public ObservableCollection<ThreadCpuRow> Threads { get; } = new();

    // One card per active camera (eye left/right or mouth).
    public ObservableCollection<CameraRow> Cameras { get; } = new();

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

        var eye = _metrics.EyeInferences;
        RenderFps = Smooth(RenderFps, (RenderTicks - _prevRender) / dt);
        UiLoopFps = Smooth(UiLoopFps, (_metrics.UiTicks - _prevUi) / dt);
        EyeInferenceFps = Smooth(EyeInferenceFps, (eye - _prevEye) / dt);
        FaceInferenceFps = Smooth(FaceInferenceFps, (_metrics.FaceInferences - _prevFace) / dt);

        UpdateCameras(now, dt);

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
        _prevEye = eye;
        _prevFace = _metrics.FaceInferences;
        _prevTimestamp = now;
    }

    private void UpdateCameras(long now, double dt)
    {
        var current = new List<(string Label, SingleCameraSource Source)>(3);
        if (_metrics.EyeDual)
        {
            if (_metrics.EyeLeftSource is { } el) current.Add(("Eye (left)", el));
            if (_metrics.EyeRightSource is { } er) current.Add(("Eye (right)", er));
        }
        else if (_metrics.EyeLeftSource is { } single)
        {
            current.Add(("Eye", single));
        }
        if (_metrics.FaceSource is { } mouth) current.Add(("Mouth", mouth));

        while (Cameras.Count < current.Count) Cameras.Add(new CameraRow());
        while (Cameras.Count > current.Count) Cameras.RemoveAt(Cameras.Count - 1);

        var active = new HashSet<SingleCameraSource>();
        for (var i = 0; i < current.Count; i++)
        {
            var (label, src) = current[i];
            active.Add(src);
            var cap = src.Capture;

            if (!_camStates.TryGetValue(src, out var st))
            {
                st = new CamState { PrevFrames = cap.FramesProduced };
                st.DropWindow.Enqueue((now, cap.FramesDropped));
                _camStates[src] = st;
            }

            var frames = cap.FramesProduced;
            st.FpsEwma = Smooth(st.FpsEwma, (frames - st.PrevFrames) / dt);
            st.PrevFrames = frames;

            var drop = cap.FramesDropped;
            st.DropWindow.Enqueue((now, drop));
            while (st.DropWindow.Count > 1 && st.DropWindow.Peek().Timestamp < now - DropWindowTicks)
                st.DropWindow.Dequeue();
            var lost = Math.Max(0, drop - st.DropWindow.Peek().Dropped);

            var row = Cameras[i];
            row.Name = label;
            row.DeliveredFps = st.FpsEwma;
            row.DroppedSummary = $"{lost} frames";
            row.NegotiatedFps = cap.TargetFps;
            row.Resolution = src.CameraSize.Width > 0 ? $"{src.CameraSize.Width} x {src.CameraSize.Height}" : "—";
            row.Format = string.IsNullOrEmpty(cap.PixelFormatName) ? "—" : cap.PixelFormatName;
        }

        // Drop state for cameras that went away (swap/disconnect).
        if (_camStates.Count > active.Count)
        {
            var stale = new List<SingleCameraSource>();
            foreach (var key in _camStates.Keys)
                if (!active.Contains(key)) stale.Add(key);
            foreach (var key in stale) _camStates.Remove(key);
        }
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
