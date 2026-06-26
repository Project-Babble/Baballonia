using Baballonia.Contracts;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Filters;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.VideoSources;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.IO;
using System.Threading.Tasks;

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

    private string? _currentLeftAddress;
    private string? _currentRightAddress;

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
        LoadSplitEyeSwap();
    }

    public async Task LoadInferenceAsync()
    {
        var inf = await Task.Run(CreateInference);
        lock (_pipeline.SyncRoot)
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
        var inf = CreateInference();
        lock (_pipeline.SyncRoot)
            _pipeline.InferenceService = inf;
    }

    public void LoadFilter()
    {
        var enabled = _localSettings.ReadSetting<bool>("AppSettings_OneEuroEnabled");
        var cutoff = _localSettings.ReadSetting<float>("AppSettings_OneEuroMinFreqCutoff");
        var speedCutoff = _localSettings.ReadSetting<float>("AppSettings_OneEuroSpeedCutoff");

        if (!enabled)
            return;

        var eyeFilter = new OneEuroFilter(
            minCutoff: cutoff,
            beta: speedCutoff
        );

        lock (_pipeline.SyncRoot)
            _pipeline.Filter = eyeFilter;
    }

    public void LoadEyeStabilization()
    {
        var stabilizeEyes = _localSettings.ReadSetting<bool>("AppSettings_StabilizeEyes", true);
        _pipeline.StabilizeEyes = stabilizeEyes;
    }

    public void LoadSplitEyeSwap()
    {
        // Default unswapped: split devices like BSB2E expect the left/right halves as-is.
        _pipeline.SwapSplitEyes = _localSettings.ReadSetting<bool>("AppSettings_SplitEyeVideoSwap", false);
    }

    public void SetLeftTransformation(CameraSettings cameraSettings)
    {
        lock (_pipeline.SyncRoot)
        {
            if (_pipeline.ImageTransformer is DualImageTransformer dualImageTransformer)
            {
                dualImageTransformer.LeftTransformer.Transformation = cameraSettings;
            }
        }
    }
    public void SetRightTransformation(CameraSettings cameraSettings)
    {
        lock (_pipeline.SyncRoot)
        {
            if (_pipeline.ImageTransformer is DualImageTransformer dualImageTransformer)
            {
                dualImageTransformer.RightTransformer.Transformation = cameraSettings;
            }
        }
    }

    public async Task<bool> StartLeftVideoSource(string cameraAddress, string preferredBackend)
    {
        if (string.IsNullOrEmpty(cameraAddress))
            return false;

        if (_pipeline.VideoSource == null)
        {
            SingleCameraSource cam;
            if (string.IsNullOrEmpty(preferredBackend))
                cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
            else
                cam = await _singleCameraSourceFactory.CreateStart(cameraAddress, preferredBackend);

            if (cam == null)
                return false;

            var source = new DualCameraSource();
            source.LeftCam = cam;
            lock (_pipeline.SyncRoot)
                _pipeline.VideoSource = source;
            _currentLeftAddress = cameraAddress;
            return true;
        }

        if (_pipeline.VideoSource is DualCameraSource dualCameraSource)
            if (cameraAddress == _currentRightAddress && _currentRightAddress != null)
            {
                var tmp = dualCameraSource.RightCam;
                lock (_pipeline.SyncRoot)
                    _pipeline.VideoSource = tmp;
                _currentLeftAddress = cameraAddress;
                return true;
            }
            else
            {
                lock (_pipeline.SyncRoot)
                {
                    if (dualCameraSource.LeftCam != null)
                    {
                        dualCameraSource.LeftCam.Dispose();
                        dualCameraSource.LeftCam = null;
                    }
                }

                var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress, preferredBackend);
                if (cam == null)
                    return false;
                lock (_pipeline.SyncRoot)
                    dualCameraSource.LeftCam = cam;
                _currentLeftAddress = cameraAddress;
                return true;
            }

        // FLAWED + currently UNUSED: builds `source` below but never assigns it to VideoSource; unreachable because TryStartLeftIfNotRunning no-ops on a SingleCameraSource.
        if (_pipeline.VideoSource is SingleCameraSource singleCameraSource)
        {
            if (_currentLeftAddress == cameraAddress && _currentLeftAddress != null)
                return true;

            var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress, preferredBackend);
            if (cam == null)
                return false;

            var tmp = singleCameraSource;
            lock (_pipeline.SyncRoot)
                _pipeline.VideoSource = null;
            var source = new DualCameraSource();
            source.LeftCam = cam;
            source.RightCam = tmp;

            _currentLeftAddress = cameraAddress;
            return true;
        }

        return true;
    }

    public async Task<bool> StartRightVideoSource(string cameraAddress, string preferredBackend)
    {
        if (string.IsNullOrEmpty(cameraAddress))
            return false;

        if (_pipeline.VideoSource == null)
        {
            SingleCameraSource cam;
            if (string.IsNullOrEmpty(preferredBackend))
                cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
            else
                cam = await _singleCameraSourceFactory.CreateStart(cameraAddress, preferredBackend);

            if (cam == null)
                return false;

            var source = new DualCameraSource();
            source.RightCam = cam;
            lock (_pipeline.SyncRoot)
                _pipeline.VideoSource = source;
            _currentRightAddress = cameraAddress;
            return true;
        }

        if (_pipeline.VideoSource is DualCameraSource dualCameraSource)
            if (cameraAddress == _currentLeftAddress && _currentLeftAddress != null)
            {
                var tmp = dualCameraSource.LeftCam;
                lock (_pipeline.SyncRoot)
                    _pipeline.VideoSource = tmp;
                _currentRightAddress = cameraAddress;
                return true;
            }
            else
            {
                lock (_pipeline.SyncRoot)
                {
                    if (dualCameraSource.RightCam != null)
                    {
                        dualCameraSource.RightCam.Dispose();
                        dualCameraSource.RightCam = null;
                    }
                }

                var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress, preferredBackend);
                if (cam == null)
                    return false;
                lock (_pipeline.SyncRoot)
                    dualCameraSource.RightCam = cam;
                _currentRightAddress = cameraAddress;
                return true;
            }

        // FLAWED + currently UNUSED: builds `source` below but never assigns it to VideoSource; unreachable because TryStartRightIfNotRunning no-ops on a SingleCameraSource.
        if (_pipeline.VideoSource is SingleCameraSource singleCameraSource)
        {
            if (_currentRightAddress == cameraAddress && _currentRightAddress != null)
                return true;

            var cam = await _singleCameraSourceFactory.CreateStart(cameraAddress, preferredBackend);
            if (cam == null)
                return false;

            var tmp = singleCameraSource;
            lock (_pipeline.SyncRoot)
                _pipeline.VideoSource = null;
            var source = new DualCameraSource();
            source.RightCam = cam;
            source.LeftCam = tmp;

            _currentRightAddress = cameraAddress;
            return true;
        }

        return true;
    }

    public async Task<bool> TryStartLeftIfNotRunning(string cameraAddress, string preferredBackend)
    {
        switch (_pipeline.VideoSource)
        {
            case SingleCameraSource singleCameraSource:
            case DualCameraSource { LeftCam: not null }:
                return true;
            default:
                return await StartLeftVideoSource(cameraAddress, preferredBackend);
        }
    }
    public async Task<bool> TryStartRightIfNotRunning(string cameraAddress, string preferredBackend)
    {
        switch (_pipeline.VideoSource)
        {
            case SingleCameraSource singleCameraSource:
            case DualCameraSource { RightCam: not null }:
                return true;
            default:
                return await StartRightVideoSource(cameraAddress, preferredBackend);
        }
    }
    public void StopLeftCamera()
    {
        _currentLeftAddress = null;
        lock (_pipeline.SyncRoot)
        {
            if (_pipeline.VideoSource is DualCameraSource dualCameraSource)
            {
                dualCameraSource.LeftCam?.Dispose();
                dualCameraSource.LeftCam = null;
            }

            if (_pipeline.VideoSource is SingleCameraSource singleCameraSource)
            {
                singleCameraSource.Dispose();
                _pipeline.VideoSource = null;
                _currentRightAddress = null;
            }
        }
    }

    public void StopRightCamera()
    {
        _currentRightAddress = null;
        lock (_pipeline.SyncRoot)
        {
            if (_pipeline.VideoSource is DualCameraSource dualCameraSource)
            {
                dualCameraSource.RightCam?.Dispose();
                dualCameraSource.RightCam = null;
            }

            if (_pipeline.VideoSource is SingleCameraSource singleCameraSource)
            {
                singleCameraSource.Dispose();
                _pipeline.VideoSource = null;
                _currentLeftAddress = null;
            }
        }
    }

    public void StopAllCameras()
    {
        _currentRightAddress = null;
        _currentLeftAddress = null;
        lock (_pipeline.SyncRoot)
        {
            _pipeline.VideoSource?.Dispose();
            _pipeline.VideoSource = null;
        }
    }

    /// <summary>
    /// Stops only the eye feed(s) backed by a serial camera, releasing the serial handle so the
    /// firmware page can open it. For a DualCameraSource each eye is evaluated independently so a
    /// non-serial eye keeps running. UVC/IP eyes are left untouched.
    /// </summary>
    public bool StopSerialCameras()
    {
        var stopped = false;
        lock (_pipeline.SyncRoot)
        {
            switch (_pipeline.VideoSource)
            {
                case SingleCameraSource single when IsSerialAddress(single.Capture?.Source):
                    single.Dispose();
                    _pipeline.VideoSource = null;
                    _currentLeftAddress = null;
                    _currentRightAddress = null;
                    stopped = true;
                    break;
                case DualCameraSource dual:
                    // Same physical serial cam used for both eyes -> one shared instance; null both
                    // refs after a single dispose so the right branch can't double-dispose it.
                    var shared = ReferenceEquals(dual.LeftCam, dual.RightCam);
                    if (dual.LeftCam is SingleCameraSource l && IsSerialAddress(l.Capture?.Source))
                    {
                        l.Dispose();
                        dual.LeftCam = null;
                        _currentLeftAddress = null;
                        stopped = true;
                        if (shared) { dual.RightCam = null; _currentRightAddress = null; }
                    }
                    if (dual.RightCam is SingleCameraSource r && IsSerialAddress(r.Capture?.Source))
                    {
                        r.Dispose();
                        dual.RightCam = null;
                        _currentRightAddress = null;
                        stopped = true;
                    }
                    if (dual.LeftCam == null && dual.RightCam == null)
                        _pipeline.VideoSource = null;
                    break;
            }
        }
        return stopped;
    }

    // Mirrors SerialCameraCaptureFactory.CanConnect: serial camera addresses are COM* / /dev/tty* / /dev/cu*.
    private static bool IsSerialAddress(string? address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        var a = address.ToLowerInvariant();
        return a.StartsWith("com") || a.StartsWith("/dev/tty") || a.StartsWith("/dev/cu");
    }

    public bool IsUsingSameCamera()
    {
        return _currentLeftAddress == _currentRightAddress && _currentLeftAddress != null;
    }

    public void SetFilter(IFilter? filter)
    {
        lock (_pipeline.SyncRoot)
            _pipeline.Filter = filter;
    }
}
