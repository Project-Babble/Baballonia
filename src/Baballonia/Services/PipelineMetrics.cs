namespace Baballonia.Services;

/// <summary>
/// Lightweight throughput counters for the processing pipeline, surfaced on the Debug page.
/// Counters are monotonic; consumers derive a rate by sampling deltas over time.
/// ProcessingLoopService writes these on the UI thread each tick (snapshotting the camera frame
/// count from the capture thread); the Debug view-model reads them on the UI thread.
/// </summary>
public sealed class PipelineMetrics
{
    // Monotonic tick/inference counters.
    public long UiTicks;
    public long EyeInferences;
    public long FaceInferences;

    // Latest eye-camera snapshot.
    public long EyeCameraFrames;
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
