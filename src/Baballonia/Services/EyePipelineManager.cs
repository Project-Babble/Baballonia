using System;
using System.IO;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Filters;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.VideoSources;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baballonia.Services;

/// <summary>
/// This class should be the only place where direct Pipeline modifications happen
/// </summary>
public class EyePipelineManager
{
    private readonly ILogger<EyePipelineManager> _logger;
    private readonly EyeProcessingPipeline _pipeline;
    private readonly ILocalSettingsService _localSettings;
    private readonly InferenceFactory _inferenceFactory;
    private readonly SingleCameraSourceFactory _singleCameraSourceFactory;

    private string? _currentLeftAdress;
    private string? _currentRightAdress;

    public EyePipelineManager(ILogger<EyePipelineManager> logger, EyeProcessingPipeline pipeline,
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
        var dualTransformer = new DualImageTransformer();
        dualTransformer.LeftTransformer.TargetSize = new Size(128, 128);
        dualTransformer.RightTransformer.TargetSize = new Size(128, 128);
        _pipeline.ImageTransformer = dualTransformer;

        _ = LoadInferenceAsync();
        LoadFilter();
        LoadEyeStabilization();
    }

    public async Task LoadInferenceAsync()
    {
        var inf = await Task.Run(CreateInference);
        _pipeline.InferenceService = inf;
    }

    private DefaultInferenceRunner CreateInference()
    {
        const string defaultEyeModelName = "eyeModel.onnx";
        var eyeModelName = _localSettings.ReadSetting<string>("EyeHome_EyeModel", defaultEyeModelName);
        var eyeModelPath = Path.Combine(AppContext.BaseDirectory, eyeModelName);

        if (File.Exists(eyeModelPath)) return _inferenceFactory.Create(eyeModelPath);
        _logger.LogError("{} Does not exists, Loading default...", eyeModelPath);
        eyeModelName = defaultEyeModelName;
        eyeModelPath = Path.Combine(AppContext.BaseDirectory, eyeModelName);

        return _inferenceFactory.Create(eyeModelPath);
    }


    public void LoadInference()
    {
        _pipeline.InferenceService = CreateInference();
    }

    public void LoadFilter()
    {
        var enabled = _localSettings.ReadSetting<bool>("AppSettings_OneEuroEnabled");
        var cutoff = _localSettings.ReadSetting<float>("AppSettings_OneEuroMinFreqCutoff");
        var speedCutoff = _localSettings.ReadSetting<float>("AppSettings_OneEuroSpeedCutoff");

        if (!enabled)
            return;

        var faceArray = new float[Utils.EyeRawExpressions];
        var faceFilter = new OneEuroFilter(
            faceArray,
            minCutoff: cutoff,
            beta: speedCutoff
        );

        _pipeline.Filter = faceFilter;
    }

    public void LoadEyeStabilization()
    {
        var stabilizeEyes = _localSettings.ReadSetting<bool>("AppSettings_StabilizeEyes", false);
        _pipeline.StabilizeEyes = stabilizeEyes;
    }

    public void SetLeftTransformation(CameraSettings cameraSettings)
    {
        if (_pipeline.ImageTransformer is DualImageTransformer dualImageTransformer)
        {
            dualImageTransformer.LeftTransformer.Transformation = cameraSettings;
        }
    }
    public void SetRightTransformation(CameraSettings cameraSettings)
    {
        if (_pipeline.ImageTransformer is DualImageTransformer dualImageTransformer)
        {
            dualImageTransformer.RightTransformer.Transformation = cameraSettings;
        }
    }

    public async Task<bool> StartLeftVideoSource(string cameraAddress)
    {
        if (_pipeline.VideoSource == null)
        {
            var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
            if (cam == null)
                return false;

            var source = new DualCameraSource();
            source.LeftCam = cam;
            _pipeline.VideoSource = source;
            _currentLeftAdress = cameraAddress;
            return true;
        }

        if (_pipeline.VideoSource is DualCameraSource dualCameraSource)
            if (cameraAddress == _currentRightAdress && _currentRightAdress != null)
            {
                var tmp = dualCameraSource.RightCam;
                _pipeline.VideoSource = tmp;
                _currentLeftAdress = cameraAddress;
                return true;
            }
            else
            {
                if (dualCameraSource.LeftCam != null)
                {
                    dualCameraSource.LeftCam.Dispose();
                    dualCameraSource.LeftCam = null;
                }

                var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
                if (cam == null)
                    return false;
                dualCameraSource.LeftCam = cam;
                _currentLeftAdress = cameraAddress;
                return true;
            }

        if (_pipeline.VideoSource is SingleCameraSource singleCameraSource)
        {
            if (_currentLeftAdress == cameraAddress && _currentLeftAdress != null)
                return true;

            var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
            if (cam == null)
                return false;

            var tmp = singleCameraSource;
            _pipeline.VideoSource = null;
            var source = new DualCameraSource();
            source.LeftCam = cam;
            source.RightCam = tmp;

            _currentLeftAdress = cameraAddress;
            return true;
        }

        return true;
    }

    public async Task<bool> StartRightVideoSource(string cameraAddress)
    {
        if (_pipeline.VideoSource == null)
        {
            var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
            if (cam == null)
                return false;

            var source = new DualCameraSource();
            source.RightCam = cam;
            _pipeline.VideoSource = source;
            _currentRightAdress = cameraAddress;
            return true;
        }

        if (_pipeline.VideoSource is DualCameraSource dualCameraSource)
            if (cameraAddress == _currentLeftAdress && _currentLeftAdress != null)
            {
                var tmp = dualCameraSource.LeftCam;
                _pipeline.VideoSource = tmp;
                _currentRightAdress = cameraAddress;
                return true;
            }
            else
            {
                if (dualCameraSource.RightCam != null)
                {
                    dualCameraSource.RightCam.Dispose();
                    dualCameraSource.RightCam = null;
                }

                var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
                if (cam == null)
                    return false;
                dualCameraSource.RightCam = cam;
                _currentRightAdress = cameraAddress;
                return true;
            }

        if (_pipeline.VideoSource is SingleCameraSource singleCameraSource)
        {
            if (_currentRightAdress == cameraAddress && _currentRightAdress != null)
                return true;

            var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
            if (cam == null)
                return false;

            var tmp = singleCameraSource;
            _pipeline.VideoSource = null;
            var source = new DualCameraSource();
            source.RightCam = cam;
            source.LeftCam = tmp;

            _currentRightAdress = cameraAddress;
            return true;
        }

        return true;
    }

    public async Task<bool> TryStartLeftIfNotRunning(string cameraAddress)
    {
        switch (_pipeline.VideoSource)
        {
            case SingleCameraSource singleCameraSource:
            case DualCameraSource { LeftCam: not null }:
                return true;
            default:
                return await StartLeftVideoSource(cameraAddress);
        }
    }
    public async Task<bool> TryStartRightIfNotRunning(string cameraAddress)
    {
        switch (_pipeline.VideoSource)
        {
            case SingleCameraSource singleCameraSource:
            case DualCameraSource { RightCam: not null }:
                return true;
            default:
                return await StartRightVideoSource(cameraAddress);
        }
    }
    public void StopLeftCamera()
    {
        _currentLeftAdress = null;
        if (_pipeline.VideoSource is DualCameraSource dualCameraSource)
        {
            dualCameraSource.LeftCam?.Dispose();
            dualCameraSource.LeftCam = null;
        }

        if (_pipeline.VideoSource is SingleCameraSource singleCameraSource)
        {
            singleCameraSource.Dispose();
            _pipeline.VideoSource = null;
            _currentRightAdress = null;
        }
    }

    public void StopRightCamera()
    {
        _currentRightAdress = null;
        if (_pipeline.VideoSource is DualCameraSource dualCameraSource)
        {
            dualCameraSource.RightCam?.Dispose();
            dualCameraSource.RightCam = null;
        }

        if (_pipeline.VideoSource is SingleCameraSource singleCameraSource)
        {
            singleCameraSource.Dispose();
            _pipeline.VideoSource = null;
            _currentLeftAdress = null;
        }
    }

    public void StopAllCameras()
    {
        _currentRightAdress = null;
        _currentLeftAdress = null;
        _pipeline.VideoSource?.Dispose();
        _pipeline.VideoSource = null;
    }

    public bool IsUsingSameCamera()
    {
        return _currentLeftAdress == _currentRightAdress && _currentLeftAdress != null;
    }

    public void SetFilter(IFilter? filter)
    {
        _pipeline.Filter = filter;
    }
}
