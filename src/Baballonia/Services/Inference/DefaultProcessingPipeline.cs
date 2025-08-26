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

    public event Action<Mat> NewFrameEvent;
    public event Action<Mat> TransformedFrameEvent;
    public event Action<float[]> FilteredResultEvent;

    protected void InvokeNewFrameEvent(Mat mat)
    {
        NewFrameEvent?.Invoke(mat);
    }

    protected void InvokeTransformedFrameEvent(Mat mat)
    {
        TransformedFrameEvent?.Invoke(mat);
    }

    protected void InvokeFilteredResultEvent(float[] arr)
    {
        FilteredResultEvent?.Invoke(arr);
    }

    public float[]? RunUpdate()
    {
        var frame = VideoSource?.GetFrame(ColorType.Gray8);
        if(frame == null)
            return null;

        NewFrameEvent?.Invoke(frame);

        var transformed = ImageTransformer?.Apply(frame);
        if(transformed == null)
            return null;

        TransformedFrameEvent?.Invoke(transformed);

        if (InferenceService == null)
            return null;

        ImageConverter?.Convert(transformed, InferenceService.GetInputTensor());

        var inferenceResult = InferenceService?.Run();
        if(inferenceResult == null)
            return null;

        if(Filter != null)
            inferenceResult = Filter.Filter(inferenceResult);

        FilteredResultEvent?.Invoke(inferenceResult);

        frame.Dispose();
        transformed.Dispose();

        return inferenceResult;
    }
}
