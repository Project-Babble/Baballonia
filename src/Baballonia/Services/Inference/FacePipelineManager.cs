using System;
using System.IO;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Filters;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.VideoSources;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services.Inference;

/// <summary>
/// This class should be the only place where direct Pipeline modifications happen
/// </summary>
public class FacePipelineManager
{
    private readonly ILogger<FacePipelineManager> _logger;
    private readonly FaceProcessingPipeline _pipeline;
    private readonly ILocalSettingsService _localSettings;
    private readonly InferenceFactory _inferenceFactory;
    private readonly SingleCameraSourceFactory _singleCameraSourceFactory;

    public FacePipelineManager(ILogger<FacePipelineManager> logger, FaceProcessingPipeline pipeline,
        ILocalSettingsService localSettings, InferenceFactory inferenceFactory,
        SingleCameraSourceFactory singleCameraSourceFactory)
    {
        _logger = logger;
        _pipeline = pipeline;
        _localSettings = localSettings;
        _inferenceFactory = inferenceFactory;
        _singleCameraSourceFactory = singleCameraSourceFactory;

        InitializePipeline();
    }

    public void InitializePipeline()
    {
        _pipeline.ImageConverter = new MatToFloatTensorConverter();
        _pipeline.ImageTransformer = new ImageTransformer();

        _ = LoadInferenceAsync();
        LoadFilter();
    }

    public async Task LoadInferenceAsync()
    {
        var inf = await Task.Run(CreateInference);
        _pipeline.InferenceService = inf;
    }

    public void LoadInference()
    {
        _pipeline.InferenceService = CreateInference();
    }

    public DefaultInferenceRunner CreateInference()
    {
        const string defaultFaceModel = "faceModel.onnx";
        return _inferenceFactory.Create(Path.Combine(AppContext.BaseDirectory, defaultFaceModel));
    }

    public void LoadFilter()
    {
        var enabled = _localSettings.ReadSetting<bool>("AppSettings_OneEuroEnabled");
        var cutoff = _localSettings.ReadSetting<float>("AppSettings_OneEuroMinFreqCutoff");
        var speedCutoff = _localSettings.ReadSetting<float>("AppSettings_OneEuroSpeedCutoff");

        if (!enabled)
            return;

        var faceArray = new float[Utils.FaceRawExpressions];
        var faceFilter = new OneEuroFilter(
            faceArray,
            minCutoff: cutoff,
            beta: speedCutoff
        );

        _pipeline.Filter = faceFilter;
    }

    public void StopCamera()
    {
        _pipeline.VideoSource?.Dispose();
        _pipeline.VideoSource = null;
    }

    public void SetVideoSource(IVideoSource videoSource)
    {
        _pipeline.VideoSource = videoSource;
    }

    public void SetTransformation(CameraSettings cameraSettings)
    {
        if (_pipeline.ImageTransformer is ImageTransformer dualImageTransformer)
        {
            dualImageTransformer.Transformation = cameraSettings;
        }
    }

    public async Task<bool> StartVideoSource(string cameraAddress, string preferredBackend)
    {
        if (string.IsNullOrEmpty(cameraAddress))
            return false;

        if (_pipeline.VideoSource != null)
        {
            _pipeline.VideoSource.Dispose();
            _pipeline.VideoSource = null;
        }

        SingleCameraSource cam;
        if (string.IsNullOrEmpty(preferredBackend))
            cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
        else
            cam = await _singleCameraSourceFactory.CreateStart(cameraAddress, preferredBackend);

        if (cam == null)
            return false;

        _pipeline.VideoSource = cam;
        return true;
    }

    public async Task<bool> TryStartIfNotRunning(string cameraAddress, string preferredBackend)
    {
        if (_pipeline.VideoSource != null)
            return true;

        return await StartVideoSource(cameraAddress, preferredBackend);
    }

    public void SetFilter(IFilter? filter)
    {
        _pipeline.Filter = filter;
    }
}
