using System;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;
using OpenCvSharp;

namespace Baballonia.Services.Inference;

public class FaceProcessingPipeline : DefaultProcessingPipeline
{

     public float[]? RunUpdate()
    {
        var frame = VideoSource?.GetFrame(ColorType.Gray8);
        if(frame == null)
            return null;

        InvokeNewFrameEvent(frame);

        var transformed = ImageTransformer?.Apply(frame);

        if(transformed == null)
            return null;

        InvokeTransformedFrameEvent(transformed);


        if (InferenceService == null)
            return null;

        ImageConverter?.Convert(transformed, InferenceService.GetInputTensor());
        transformed.Dispose();

        var inferenceResult = InferenceService?.Run();
        if(inferenceResult == null)
            return null;

        if(Filter != null)
            inferenceResult = Filter.Filter(inferenceResult);

        InvokeFilteredResultEvent(inferenceResult);


        return inferenceResult;
    }
}
