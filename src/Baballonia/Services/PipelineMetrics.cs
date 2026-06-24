using Baballonia.SDK;

namespace Baballonia.Services;

/// <summary>
/// Monotonic throughput counters for the pipeline, surfaced on the Debug page. Producers just increment
/// on their own thread; the Debug view-model samples deltas against a monotonic clock to derive rates.
/// </summary>
public sealed class PipelineMetrics
{
    public long UiTicks;
    public long EyeInferences;
    public long FaceInferences;

    // Active eye capture; the sampler reads FramesProduced off this live. Written on a worker, read on UI.
    public volatile Capture? EyeCapture;

    public double EyeCameraTargetFps;
    public int EyeCameraWidth;
    public int EyeCameraHeight;
    public string EyeCameraFormat = "";

    // Per-stage processing time (EWMA, milliseconds). Written by the respective inference worker while
    // it holds the pipeline's SyncRoot (so writes are serialised); read on the UI thread for display.
    // Together with per-thread CPU%, these answer "which stage of a hot thread is the hotspot".
    public double EyeCaptureMs;
    public double EyeTransformMs;
    public double EyeInferenceMs;
    public double EyePostMs;

    public double FaceCaptureMs;
    public double FaceTransformMs;
    public double FaceInferenceMs;
    public double FacePostMs;

    /// <summary>Exponential moving average that seeds from the first sample (so it converges quickly).</summary>
    public static double Ewma(double previous, double sample) =>
        previous <= 0 ? sample : previous * 0.9 + sample * 0.1;
}
