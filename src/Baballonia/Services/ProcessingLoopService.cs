using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Services.events;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Filters;
using CommunityToolkit.Mvvm.DependencyInjection;
using HarfBuzzSharp;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baballonia.Services;

public class ProcessingLoopService : IDisposable
{
    public record struct Expressions(float[]? FaceExpression, float[]? EyeExpression);

    public event Action<Expressions> ExpressionChangeEvent;

    private readonly ILogger<ProcessingLoopService> _logger;
    private readonly FaceProcessingPipeline _faceProcessingPipeline;
    private readonly FacePipelineManager _facePipelineManager;
    private readonly IFacePipelineEventBus _facePipelineEventBus;
    private readonly EyeProcessingPipeline _eyeProcessingPipeline;
    private readonly EyePipelineManager _eyePipelineManager;
    private readonly IEyePipelineEventBus _eyePipelineEventBus;

    private readonly DispatcherTimer _drawTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(10)
    };


    public ProcessingLoopService(
        ILogger<ProcessingLoopService> logger,
        EyeProcessingPipeline eyeProcessingPipeline, FaceProcessingPipeline faceProcessingPipeline,
        IFacePipelineEventBus facePipelineEventBus, IEyePipelineEventBus eyePipelineEventBus,
        FacePipelineManager facePipelineManager, EyePipelineManager eyePipelineManager)
    {
        _logger = logger;
        _eyeProcessingPipeline = eyeProcessingPipeline;
        _faceProcessingPipeline = faceProcessingPipeline;
        _facePipelineEventBus = facePipelineEventBus;
        _eyePipelineEventBus = eyePipelineEventBus;
        _facePipelineManager = facePipelineManager;
        _eyePipelineManager = eyePipelineManager;

        _drawTimer.Tick += TimerEvent;
        _drawTimer.Start();
    }

    private void TimerEvent(object? s, EventArgs e)
    {
        var expressions = new Expressions();

        try
        {
            var faceExpression = _faceProcessingPipeline.RunUpdate();
            if (faceExpression != null)
                expressions.FaceExpression = faceExpression;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected exception in Face Tracking pipeline, stopping... : {}", ex);
            _facePipelineManager.StopCamera();
            _facePipelineEventBus.Publish(new FacePipelineEvents.ExceptionEvent(ex));
        }

        try
        {
            var eyeExpression = _eyeProcessingPipeline.RunUpdate();
            if (eyeExpression != null)
                expressions.EyeExpression = eyeExpression;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected exception in Eye Tracking pipeline, stopping... : {}", ex);
            _eyePipelineManager.StopAllCameras();
            _eyePipelineEventBus.Publish(new EyePipelineEvents.ExceptionEvent(ex));
        }

        if (expressions.FaceExpression != null || expressions.EyeExpression != null)
            ExpressionChangeEvent?.Invoke(expressions);
    }

    public void Start()
    {
        _drawTimer.Start();
    }

    public void Pause()
    {
        _drawTimer.Stop();
    }

    public void Dispose()
    {
        _drawTimer.Stop();
        _faceProcessingPipeline.VideoSource?.Dispose();
        _eyeProcessingPipeline.VideoSource?.Dispose();
    }
}
