using System;
using Baballonia.Services.events;
using Baballonia.Services.Inference.Enums;

namespace Baballonia.Services.Inference;

public class EyeProcessingPipeline : DefaultProcessingPipeline, IDisposable
{
    private readonly IEyePipelineEventBus _eyePipelineEventBus;
    private readonly FastCorruptionDetector.FastCorruptionDetector _fastCorruptionDetector = new();
    private readonly ImageCollector _imageCollector = new();

    public EyeProcessingPipeline(IEyePipelineEventBus eyePipelineEventBus)
    {
        _eyePipelineEventBus = eyePipelineEventBus;
    }

    public bool StabilizeEyes { get; set; } = true;

    public float[]? RunUpdate()
    {
        var frame = VideoSource?.GetFrame(ColorType.Gray8);
        if(frame == null)
            return null;

        if (_fastCorruptionDetector.IsCorrupted(frame).isCorrupted)
            return null;

        _eyePipelineEventBus.Publish(new EyePipelineEvents.NewFrameEvent(frame));

        var transformed = ImageTransformer?.Apply(frame);
        if(transformed == null)
            return null;

        _eyePipelineEventBus.Publish(new EyePipelineEvents.NewTransformedFrameEvent(transformed));

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

        if(Filter != null)
            inferenceResult = Filter.Filter(inferenceResult);

        ProcessExpressions(ref inferenceResult);

        _eyePipelineEventBus.Publish(new EyePipelineEvents.NewFilteredResultEvent(inferenceResult));

        frame.Dispose();
        transformed.Dispose();

        return inferenceResult;
    }

    private bool ProcessExpressions(ref float[] arKitExpressions)
    {
        if (arKitExpressions.Length < Utils.EyeRawExpressions)
            return false;

        const float mulV = 2.0f;
        const float mulY = 2.0f;

        var leftPitch = arKitExpressions[0] * mulY - mulY / 2;
        var leftYaw = arKitExpressions[1] * mulV - mulV / 2;
        var leftLid = 1 - arKitExpressions[2];

        var rightPitch = arKitExpressions[3] * mulY - mulY / 2;
        var rightYaw = arKitExpressions[4] * mulV - mulV / 2;
        var rightLid = 1 - arKitExpressions[5];

        var eyeY = (leftPitch * leftLid + rightPitch * rightLid) / (leftLid + rightLid);

        var leftEyeYawCorrected = rightYaw * (1 - leftLid) + leftYaw * leftLid;
        var rightEyeYawCorrected = leftYaw * (1 - rightLid) + rightYaw * rightLid;

        if (StabilizeEyes)
        {
            var rawConvergence = (rightEyeYawCorrected - leftEyeYawCorrected) / 2.0f;
            var convergence = Math.Max(rawConvergence, 0.0f); //We clamp the value here to avoid accidental divergence, as the model sometimes decides that's a thing

            var averagedYaw = (rightEyeYawCorrected + leftEyeYawCorrected) / 2.0f;

            leftEyeYawCorrected = averagedYaw - convergence;
            rightEyeYawCorrected = averagedYaw + convergence;
        }

        // [left pitch, left yaw, left lid...
        float[] convertedExpressions = new float[Utils.EyeRawExpressions];

        // swap eyes at this point
        convertedExpressions[0] = rightEyeYawCorrected; // left pitch
        convertedExpressions[1] = eyeY;                   // left yaw
        convertedExpressions[2] = rightLid;               // left lid
        convertedExpressions[3] = leftEyeYawCorrected;  // right pitch
        convertedExpressions[4] = eyeY;                   // right yaw
        convertedExpressions[5] = leftLid;                // right lid

        arKitExpressions = convertedExpressions;

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
