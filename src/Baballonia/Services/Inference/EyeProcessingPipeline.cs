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

    public OrderedFloatMap? RunUpdate()
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

        if (Filter != null)
        {
            inferenceResult = Filter.Filter(inferenceResult);
        }

        ProcessExpressions(ref inferenceResult);

        eyePipelineEventBus.Publish(new EyePipelineEvents.NewFilteredResultEvent(inferenceResult));

        frame.Dispose();
        transformed.Dispose();

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

        if (StabilizeEyes && false)
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
