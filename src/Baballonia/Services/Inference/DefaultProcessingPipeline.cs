using System;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;
using OpenCvSharp;

namespace Baballonia.Services.Inference;

public interface IProcessingPipeline
{
    float[]? RunUpdate();
}
public class DefaultProcessingPipeline : IProcessingPipeline
{
    public IVideoSource? VideoSource;
    public IImageTransformer? ImageTransformer;
    public IImageConverter? ImageConverter;
    public IInferenceRunner? InferenceService;
    public IFilter? Filter;



    public float[]? RunUpdate()
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
            inferenceResult = Filter.Filter(inferenceResult);

        frame.Dispose();
        transformed.Dispose();

        return inferenceResult;
    }
}
