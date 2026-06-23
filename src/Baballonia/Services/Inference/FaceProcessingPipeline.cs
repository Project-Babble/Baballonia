using Baballonia.Services.events;
using Baballonia.Services.Inference.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Baballonia.Services.Inference;

public class FaceProcessingPipeline(IFacePipelineEventBus facePipelineEventBus, PipelineMetrics metrics) : DefaultProcessingPipeline
{
    public OrderedFloatMap? RunUpdate()
    {
        var sw = Stopwatch.StartNew();

        // `frame` is owned by us; `using` frees it on every exit path. Subscribers copy the Mat
        // synchronously during Publish, so disposing afterwards is safe.
        using var frame = VideoSource?.GetFrame(ColorType.Gray8);
        if(frame == null)
            return null;

        facePipelineEventBus.Publish(new FacePipelineEvents.NewFrameEvent(frame));
        metrics.FaceCaptureMs = PipelineMetrics.Ewma(metrics.FaceCaptureMs, sw.Elapsed.TotalMilliseconds);

        sw.Restart();
        var transformed = ImageTransformer?.Apply(frame);

        if(transformed == null)
            return null;

        facePipelineEventBus.Publish(new FacePipelineEvents.NewTransformedFrameEvent(transformed));
        metrics.FaceTransformMs = PipelineMetrics.Ewma(metrics.FaceTransformMs, sw.Elapsed.TotalMilliseconds);

        if (InferenceService == null)
        {
            transformed.Dispose();
            return null;
        }

        sw.Restart();
        ImageConverter?.Convert(transformed, InferenceService.GetInputTensor());
        transformed.Dispose();

        var inferenceResult = InferenceService?.Run();
        if(inferenceResult == null)
            return null;
        metrics.FaceInferenceMs = PipelineMetrics.Ewma(metrics.FaceInferenceMs, sw.Elapsed.TotalMilliseconds);

        sw.Restart();
        if(Filter != null)
            inferenceResult = Filter.Filter(inferenceResult);

        facePipelineEventBus.Publish(new FacePipelineEvents.NewFilteredResultEvent(inferenceResult));
        metrics.FacePostMs = PipelineMetrics.Ewma(metrics.FacePostMs, sw.Elapsed.TotalMilliseconds);

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
