using System;
using Baballonia.Contracts;
using Baballonia.Services.events;
using Baballonia.Services.Inference.Enums;
using OpenCvSharp;

namespace Baballonia.Services.Inference;

public class FaceProcessingPipeline : DefaultProcessingPipeline
{

    private readonly IFacePipelineEventBus _facePipelineEventBus;

    public FaceProcessingPipeline(IFacePipelineEventBus facePipelineEventBus)
    {
        _facePipelineEventBus = facePipelineEventBus;
    }


    public float[]? RunUpdate()
    {
        var frame = VideoSource?.GetFrame(ColorType.Gray8);
        if(frame == null)
            return null;

        _facePipelineEventBus.Publish(new FacePipelineEvents.NewFrameEvent(frame));

        var transformed = ImageTransformer?.Apply(frame);

        if(transformed == null)
            return null;

        _facePipelineEventBus.Publish(new FacePipelineEvents.NewTransformedFrameEvent(transformed));

        if (InferenceService == null)
            return null;

        ImageConverter?.Convert(transformed, InferenceService.GetInputTensor());
        transformed.Dispose();

        var inferenceResult = InferenceService?.Run();
        if(inferenceResult == null)
            return null;

        if(Filter != null)
            inferenceResult = Filter.Filter(inferenceResult);

        _facePipelineEventBus.Publish(new FacePipelineEvents.NewFilteredResultEvent(inferenceResult));


        return inferenceResult;
    }

    public void Dispose()
    {
        TryDisposeObject(VideoSource);
        TryDisposeObject(ImageTransformer);
        TryDisposeObject(ImageConverter);
        TryDisposeObject(InferenceService);
        TryDisposeObject(Filter);
    }

    private void TryDisposeObject(object? obj)
    {
        (obj as IDisposable)?.Dispose();
    }
}
