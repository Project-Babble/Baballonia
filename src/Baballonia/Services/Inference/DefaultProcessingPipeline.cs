using System.Collections.Generic;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;

namespace Baballonia.Services.Inference;

public interface IProcessingPipeline
{
    OrderedFloatMap? RunUpdate();
}
public class DefaultProcessingPipeline : IProcessingPipeline
{
    public IVideoSource? VideoSource;
    public IImageTransformer? ImageTransformer;
    public IImageConverter? ImageConverter;
    public IInferenceRunner? InferenceService;
    public IFilter? Filter;

    /// <summary>
    /// Guards a <see cref="RunUpdate"/> against concurrent reconfiguration of the pipeline's mutable
    /// stages (VideoSource / InferenceService / Filter / …). The processing worker holds this for the
    /// duration of a frame; the pipeline managers hold it while swapping or disposing a stage, so a
    /// camera/model swap can never tear an object out from under an in-flight inference. Monitor
    /// re-entrancy keeps the worker's exception path (which calls back into a manager) deadlock-free.
    /// </summary>
    public readonly object SyncRoot = new();

    public OrderedFloatMap? RunUpdate()
    {
        var frame = VideoSource?.GetFrame(ColorType.Gray8);
        if(frame == null)
            return null;


        var transformed = ImageTransformer?.Apply(frame);
        if(transformed == null)
            return null;


        if (InferenceService == null)
            return null;

        ImageConverter?.Convert(transformed, InferenceService.GetInputTensor());

        var inferenceResult = InferenceService?.Run();
        if(inferenceResult == null)
            return null;

        if(Filter != null)
            inferenceResult = Filter.Filter(inferenceResult, VideoSource?.FrameIntervalSeconds ?? 0);

        frame.Dispose();
        transformed.Dispose();

        return inferenceResult;
    }
}
