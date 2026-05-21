using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace Baballonia.Services.events;

public class FacePipelineEvents
{
    public record NewFrameEvent(Mat image);

    public record NewTransformedFrameEvent(Mat image);

    public record NewFilteredResultEvent(Dictionary<string, float> result);

    public record ExceptionEvent(Exception exception);
}
public class EyePipelineEvents
{
    public record NewFrameEvent(Mat image);

    public record NewTransformedFrameEvent(Mat image);

    public record NewFilteredResultEvent(Dictionary<string, float> result);

    public record ExceptionEvent(Exception exception);
}
