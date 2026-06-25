using System;
using System.Collections.Generic;
using System.Diagnostics;
using Baballonia.Services.events;
using Baballonia.Services.Inference.Enums;

namespace Baballonia.Services.Inference;

public class EyeProcessingPipeline(IEyePipelineEventBus eyePipelineEventBus, PipelineMetrics metrics) : DefaultProcessingPipeline, IDisposable
{
    private readonly FastCorruptionDetector.FastCorruptionDetector _fastCorruptionDetector = new();
    private readonly ImageCollector _imageCollector = new();

    public bool StabilizeEyes { get; set; } = true;

    /// <summary>
    /// For single-camera (split) eye feeds, swap which half drives which eye. Off by default — most
    /// split devices (e.g. BSB2E) want the unswapped orientation. User-controlled via the
    /// "Split Eye Video Swap" advanced setting; has no effect on dual-camera setups.
    /// </summary>
    public bool SwapSplitEyes { get; set; }

    /// <summary>
    /// The raw (un-smoothed) result of the most recent <see cref="RunUpdate"/> — geometry-corrected
    /// exactly like the returned map but with the OneEuroFilter skipped. Native eye tracking (DFR /
    /// VRChat native) reads this for lowest latency. Shares RunUpdate's reused-buffer lifetime: only
    /// valid until the next RunUpdate on this pipeline.
    /// </summary>
    public OrderedFloatMap? RawEyeResult { get; private set; }

    public OrderedFloatMap? RunUpdate()
    {
        var sw = Stopwatch.StartNew();

        // `frame` is owned by us (AcquireRawMat contract); `using` frees it on every exit path.
        // Subscribers to the published events copy the Mat synchronously during Publish, so it is
        // safe to dispose afterwards.
        using var frame = VideoSource?.GetFrame(ColorType.Gray8);
        if(frame == null)
            return null;

        if (_fastCorruptionDetector.IsCorrupted(frame).isCorrupted)
            return null;

        eyePipelineEventBus.Publish(new EyePipelineEvents.NewFrameEvent(frame));
        metrics.EyeCaptureMs = PipelineMetrics.Ewma(metrics.EyeCaptureMs, sw.Elapsed.TotalMilliseconds);

        // A single-camera (split-eye) feed drives both eyes from one sensor. Whether to swap which
        // half feeds which eye is user-controlled (SwapSplitEyes / "Split Eye Video Swap"), defaulting
        // to unswapped. Dual-camera setups assign each eye explicitly, so they are always left as-is.
        if (ImageTransformer is DualImageTransformer splitTransformer)
            splitTransformer.SwapEyes = VideoSource is VideoSources.SingleCameraSource && SwapSplitEyes;

        sw.Restart();
        var transformed = ImageTransformer?.Apply(frame);
        if(transformed == null)
            return null;

        eyePipelineEventBus.Publish(new EyePipelineEvents.NewTransformedFrameEvent(transformed));

        // ImageCollector copies `transformed` into its temporal queue, so free it right away.
        // `collected` (the 8-channel temporal stack) is owned by us; `using` frees it on all paths.
        using var collected = _imageCollector.Apply(transformed);
        transformed.Dispose();
        if (collected == null)
            return null;
        metrics.EyeTransformMs = PipelineMetrics.Ewma(metrics.EyeTransformMs, sw.Elapsed.TotalMilliseconds);

        if (InferenceService == null)
            return null;

        sw.Restart();
        ImageConverter?.Convert(collected, InferenceService.GetInputTensor());

        var inferenceResult = InferenceService?.Run();
        if(inferenceResult == null)
            return null;
        metrics.EyeInferenceMs = PipelineMetrics.Ewma(metrics.EyeInferenceMs, sw.Elapsed.TotalMilliseconds);

        sw.Restart();
        // OneEuroFilter returns its own buffer and leaves the input untouched, so the runner's map
        // still holds the raw values. Process both: the raw map feeds native eye tracking (DFR), the
        // filtered map feeds VRCFT/UI as before.
        OrderedFloatMap? rawForDfr = null;
        if (Filter != null)
        {
            var filtered = Filter.Filter(inferenceResult);
            ProcessExpressions(ref inferenceResult);
            rawForDfr = inferenceResult;
            inferenceResult = filtered;
        }

        ProcessExpressions(ref inferenceResult);
        RawEyeResult = rawForDfr ?? inferenceResult; // filter off: raw == filtered

        eyePipelineEventBus.Publish(new EyePipelineEvents.NewFilteredResultEvent(inferenceResult));
        metrics.EyePostMs = PipelineMetrics.Ewma(metrics.EyePostMs, sw.Elapsed.TotalMilliseconds);

        return inferenceResult;
    }

    private bool ProcessExpressions(ref OrderedFloatMap arKitExpressions)
    {

        
        const float mulV = 2.0f;
        const float mulY = 2.0f;

        var leftX = arKitExpressions["/leftEyeX"] * mulY - mulY / 2;
        var leftY = arKitExpressions["/leftEyeY"] * mulV - mulV / 2;
        var leftLid = 1 - arKitExpressions["/leftEyeLid"];

        var rightX = arKitExpressions["/rightEyeX"] * mulY - mulY / 2;
        var rightY = arKitExpressions["/rightEyeY"] * mulV - mulV / 2;
        var rightLid = 1 - arKitExpressions["/rightEyeLid"];

        var eyeY = (leftY * leftLid + rightY * rightLid) / (leftLid + rightLid);

        var leftEyeXCorrected = rightX * (1 - leftLid) + leftX * leftLid;
        var rightEyeXCorrected = leftX * (1 - rightLid) + rightX * rightLid;

        if (StabilizeEyes)
        {
            var rawConvergence = (leftEyeXCorrected - rightEyeXCorrected) / 2.0f;
            var convergence = Math.Max(rawConvergence, 0.0f); // We clamp the value here to avoid accidental divergence, as the model sometimes decides that's a thing

            var averagedX = (rightEyeXCorrected + leftEyeXCorrected) / 2.0f;

            leftEyeXCorrected = averagedX + convergence;
            rightEyeXCorrected = averagedX - convergence;
        }

        // update the dict
        arKitExpressions["/leftEyeX"] = leftEyeXCorrected;
        arKitExpressions["/leftEyeY"] = eyeY;

        arKitExpressions["/rightEyeX"] = rightEyeXCorrected;
        arKitExpressions["/rightEyeY"] = eyeY;

        arKitExpressions["/leftEyeLid"] = leftLid;
        arKitExpressions["/rightEyeLid"] = rightLid;

        //try{

        //arKitExpressions["/leftEyeWiden"] = arKitExpressions["/rightEyeWiden"] = (arKitExpressions["/leftEyeWiden"] = arKitExpressions["/rightEyeWiden"]) / 2;
        //arKitExpressions["/leftEyeSquint"] = arKitExpressions["/rightEyeSquint"] = (arKitExpressions["/leftEyeSquint"] = arKitExpressions["/rightEyeSquint"]) / 2;

        //}catch{}

        return true;
    }


    public void Dispose()
    {
        TryDisposeObject(VideoSource);
        TryDisposeObject(ImageTransformer);
        TryDisposeObject(ImageConverter);
        TryDisposeObject(InferenceService);
        TryDisposeObject(Filter);
        TryDisposeObject(_fastCorruptionDetector);
        TryDisposeObject(_imageCollector);
    }

    private void TryDisposeObject(object? obj)
    {
        (obj as IDisposable)?.Dispose();
    }
}
