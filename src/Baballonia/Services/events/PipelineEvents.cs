using System;
using OpenCvSharp;

namespace Baballonia.Services.events;

public class FacePipelineEvents
{
    public record NewFrameEvent(Mat image);

    public record NewTransformedFrameEvent(Mat image);

    public record NewFilteredResultEvent(float[] result);

    public record ExceptionEvent(Exception exception);
}
public class EyePipelineEvents
{
    public record NewFrameEvent(Mat image);

    public record NewTransformedFrameEvent(Mat image);

    public record NewFilteredResultEvent(float[] result);

    public record ExceptionEvent(Exception exception);
}
