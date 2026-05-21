using System;
using System.Collections.Generic;
using Baballonia.Services.events;
using Baballonia.Services.Inference.Enums;

namespace Baballonia.Services.Inference;

public class EyeProcessingPipeline(IEyePipelineEventBus eyePipelineEventBus) : DefaultProcessingPipeline, IDisposable
{
    private readonly FastCorruptionDetector.FastCorruptionDetector _fastCorruptionDetector = new();
    private readonly ImageCollector _imageCollector = new();

    public bool StabilizeEyes { get; set; } = true;

    public Dictionary<string, float>? RunUpdate()
    {
        var frame = VideoSource?.GetFrame(ColorType.Gray8);
        if(frame == null)
            return null;

        if (_fastCorruptionDetector.IsCorrupted(frame).isCorrupted)
            return null;

        eyePipelineEventBus.Publish(new EyePipelineEvents.NewFrameEvent(frame));

        var transformed = ImageTransformer?.Apply(frame);
        if(transformed == null)
            return null;

        eyePipelineEventBus.Publish(new EyePipelineEvents.NewTransformedFrameEvent(transformed));

        var collected = _imageCollector.Apply(transformed);
        transformed.Dispose();
        if (collected == null)
            return null;

        if (InferenceService == null)
            return null;

        ImageConverter?.Convert(collected, InferenceService.GetInputTensor());

        var inferenceResult = InferenceService?.Run();
        if(inferenceResult == null)
            return null;

        ProcessExpressions(ref inferenceResult);

        eyePipelineEventBus.Publish(new EyePipelineEvents.NewFilteredResultEvent(inferenceResult));

        frame.Dispose();
        transformed.Dispose();

        return inferenceResult;
    }

    private bool ProcessExpressions(ref Dictionary<string, float> arKitExpressions)
    {

        const float mulV = 2.0f;
        const float mulY = 2.0f;

        var leftPitch = arKitExpressions["/leftEyeX"] * mulY - mulY / 2;
        var leftYaw = arKitExpressions["/leftEyeY"] * mulV - mulV / 2;
        var leftLid = 1 - arKitExpressions["/leftEyeLid"];

        var rightPitch = arKitExpressions["/rightEyeX"] * mulY - mulY / 2;
        var rightYaw = arKitExpressions["/rightEyeY"] * mulV - mulV / 2;
        var rightLid = 1 - arKitExpressions["/rightEyeLid"];

        var eyePitch = (leftPitch * leftLid + rightPitch * rightLid) / (leftLid + rightLid);

        var leftEyeYawCorrected = rightYaw * (1 - leftLid) + leftYaw * leftLid;
        var rightEyeYawCorrected = leftYaw * (1 - rightLid) + rightYaw * rightLid;

        if (StabilizeEyes)
        {
            var rawConvergence = (rightEyeYawCorrected - leftEyeYawCorrected) / 2.0f;
            var convergence = Math.Max(rawConvergence, 0.0f); // We clamp the value here to avoid accidental divergence, as the model sometimes decides that's a thing

            var averagedYaw = (rightEyeYawCorrected + leftEyeYawCorrected) / 2.0f;

            leftEyeYawCorrected = averagedYaw - convergence;
            rightEyeYawCorrected = averagedYaw + convergence;
        }

        // update the dict
        arKitExpressions["/leftEyeX"] = eyePitch;
        arKitExpressions["/leftEyeY"] = leftEyeYawCorrected;

        arKitExpressions["/rightEyeX"] = eyePitch;
        arKitExpressions["/rightEyeY"] = rightEyeYawCorrected;

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
