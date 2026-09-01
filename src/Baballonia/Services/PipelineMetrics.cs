using Baballonia.Services.Inference.VideoSources;

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

    // Active camera sources, published by the workers; the sampler reads frame stats off them live.
    // A single split eye feed sets only EyeLeftSource (EyeDual false); two independent feeds set both.
    public volatile SingleCameraSource? EyeLeftSource;
    public volatile SingleCameraSource? EyeRightSource;
    public volatile bool EyeDual;
    public volatile SingleCameraSource? FaceSource;

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
