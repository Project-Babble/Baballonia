using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Baballonia.Contracts;
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

    public readonly FaceProcessingPipeline FaceProcessingPipeline = new();
    public readonly EyeProcessingPipeline EyesProcessingPipeline = new();

    private readonly ILocalSettingsService _localSettingsService;

    public event Action<Exception> PipelineExceptionEvent;

    private readonly DispatcherTimer _drawTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(10)
    };

    private readonly ILogger<ProcessingLoopService> _logger;

    public ProcessingLoopService(
        ILogger<ProcessingLoopService> logger,
        ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        _logger = logger;

        FaceProcessingPipeline.ImageConverter = new MatToFloatTensorConverter();
        FaceProcessingPipeline.ImageTransformer = new ImageTransformer();

        EyesProcessingPipeline.ImageConverter = new MatToFloatTensorConverter();

        var dualTransformer = new DualImageTransformer();
        dualTransformer.LeftTransformer.TargetSize = new Size(128, 128);
        dualTransformer.RightTransformer.TargetSize = new Size(128, 128);
        EyesProcessingPipeline.ImageTransformer = dualTransformer;

        var face = LoadFaceInference();
        var eyes = LoadEyeInference();

        FaceProcessingPipeline.InferenceService = face;
        EyesProcessingPipeline.InferenceService = eyes;

        var faceFilter = LoadFaceFilter();
        var eyeFilter = LoadEyeFilter();
        FaceProcessingPipeline.Filter = faceFilter;
        EyesProcessingPipeline.Filter = eyeFilter;
        LoadEyeStabilizationSetting();

        _drawTimer.Tick += TimerEvent;
        _drawTimer.Start();
    }

    private IFilter? LoadFaceFilter()
    {
        var enabled = _localSettingsService.ReadSetting<bool>("AppSettings_OneEuroEnabled");
        var cutoff = _localSettingsService.ReadSetting<float>("AppSettings_OneEuroMinFreqCutoff");
        var speedCutoff = _localSettingsService.ReadSetting<float>("AppSettings_OneEuroSpeedCutoff");

        if (!enabled)
            return null;

        float[] faceArray = new float[Utils.FaceRawExpressions];
        var faceFilter = new OneEuroFilter(
            faceArray,
            minCutoff: cutoff,
            beta: speedCutoff
        );

        return faceFilter;
    }

    private IFilter? LoadEyeFilter()
    {
        var enabled = _localSettingsService.ReadSetting<bool>("AppSettings_OneEuroEnabled");
        var cutoff = _localSettingsService.ReadSetting<float>("AppSettings_OneEuroMinFreqCutoff");
        var speedCutoff = _localSettingsService.ReadSetting<float>("AppSettings_OneEuroSpeedCutoff");

        if (!enabled)
            return null;

        float[] eyeArray = new float[Utils.EyeRawExpressions];
        var eyeFilter = new OneEuroFilter(
            eyeArray,
            minCutoff: cutoff,
            beta: speedCutoff
        );
        return eyeFilter;
    }


    public Task<DefaultInferenceRunner> LoadEyeInferenceAsync()
    {
        return Task.Run(LoadEyeInference);
    }

    public DefaultInferenceRunner LoadEyeInference()
    {
        const string defaultEyeModel = "eyeModel.onnx";
        var eyeModel = _localSettingsService.ReadSetting<string>("EyeHome_EyeModel", defaultEyeModel);
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, eyeModel)))
        {
            _logger.LogError("{} Does not exists", eyeModel);
            eyeModel = defaultEyeModel;
        }

        if (eyeModel == defaultEyeModel)
        {
            _logger.LogDebug("Loaded default eye model with hash {EyeModelHash}", Utils.GenerateMD5(eyeModel));
        }

        var useGpu = _localSettingsService.ReadSetting<bool>("AppSettings_UseGPU", false);

        var l = Ioc.Default.GetService<ILogger<DefaultInferenceRunner>>()!;
        var eyeInference = new DefaultInferenceRunner(l);
        eyeInference.Setup(eyeModel, useGpu);

        return eyeInference;
    }

    public Task<DefaultInferenceRunner> LoadFaceInferenceAsync()
    {
        return Task.Run(LoadFaceInference);
    }

    public DefaultInferenceRunner LoadFaceInference()
    {
        var useGpu = _localSettingsService.ReadSetting<bool>("AppSettings_UseGPU", false);
        var l = Ioc.Default.GetService<ILogger<DefaultInferenceRunner>>()!;
        var faceInference = new DefaultInferenceRunner(l);

        const string defaultFaceModel = "faceModel.onnx";
        faceInference.Setup(defaultFaceModel, useGpu);
        _logger.LogDebug("Loaded default face model with hash {FaceModelHash}", Utils.GenerateMD5(defaultFaceModel));

        return faceInference;
    }

    private void LoadEyeStabilizationSetting()
    {
        var stabilizeEyes = _localSettingsService.ReadSetting<bool>("AppSettings_StabilizeEyes", false);
        EyesProcessingPipeline.StabilizeEyes = stabilizeEyes;
    }

    private void TimerEvent(object? s, EventArgs e)
    {
        var expressions = new Expressions();

        try
        {
            var faceExpression = FaceProcessingPipeline.RunUpdate();
            if (faceExpression != null)
                expressions.FaceExpression = faceExpression;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected exception in Face Tracking pipeline, stopping... : {}", ex);
            FaceProcessingPipeline.VideoSource?.Dispose();
            FaceProcessingPipeline.VideoSource = null;
            PipelineExceptionEvent?.Invoke(ex);
        }

        try
        {
            var eyeExpression = EyesProcessingPipeline.RunUpdate();
            if (eyeExpression != null)
                expressions.EyeExpression = eyeExpression;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected exception in Eye Tracking pipeline, stopping... : {}", ex);
            EyesProcessingPipeline.VideoSource?.Dispose();
            EyesProcessingPipeline.VideoSource = null;
            PipelineExceptionEvent?.Invoke(ex);
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
        FaceProcessingPipeline.VideoSource?.Dispose();
        EyesProcessingPipeline.VideoSource?.Dispose();
    }
}
