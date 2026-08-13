using Baballonia.Contracts;
using Baballonia.Services.Inference.Filters;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.VideoSources;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

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
        lock (_pipeline.SyncRoot)
            _pipeline.InferenceService = inf;
    }

    public void LoadInference()
    {
        var inf = CreateInference();
        lock (_pipeline.SyncRoot)
            _pipeline.InferenceService = inf;
    }

    public DefaultInferenceRunner CreateInference()
    {
        const string defaultFaceModel = "faceModel.onnx";
        return _inferenceFactory.Create(Path.Combine(AppContext.BaseDirectory, defaultFaceModel));
    }

    public void LoadFilter()
    {
        var filterEnabled = _localSettings.ReadSetting("AppSettings_FaceOneEuroEnabled", true);
        var minCutoff = _localSettings.ReadSetting("AppSettings_FaceOneEuroMinFreqCutoff", 0.5f);
        var beta = _localSettings.ReadSetting("AppSettings_FaceOneEuroSpeedCutoff", 3f);

        IFilter? faceFilter = filterEnabled
            ? new OneEuroFilter(minCutoff, beta)
            : null;

        lock (_pipeline.SyncRoot)
            _pipeline.Filter = faceFilter;
    }

    public void StopCamera()
    {
        lock (_pipeline.SyncRoot)
        {
            _pipeline.VideoSource?.Dispose();
            _pipeline.VideoSource = null;
        }
    }

    /// <summary>
    /// Stops the running video source ONLY if it is backed by a serial camera (its capture
    /// address looks like a COM/tty serial port), releasing the exclusive serial handle so the
    /// firmware page can open it. UVC (/dev/videoN) and IP feeds are left running.
    /// </summary>
    public bool StopSerialCameras()
    {
        lock (_pipeline.SyncRoot)
        {
            if (_pipeline.VideoSource is SingleCameraSource single && IsSerialAddress(single.Capture?.Source))
            {
                _pipeline.VideoSource.Dispose();
                _pipeline.VideoSource = null;
                return true;
            }
        }
        return false;
    }

    // Mirrors SerialCameraCaptureFactory.CanConnect (the main project can't reference that type):
    // serial camera addresses are COM* / /dev/tty* / /dev/cu*.
    internal static bool IsSerialAddress(string? address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        var a = address.ToLowerInvariant();
        return a.StartsWith("com") || a.StartsWith("/dev/tty") || a.StartsWith("/dev/cu");
    }

    public void SetVideoSource(IVideoSource videoSource)
    {
        lock (_pipeline.SyncRoot)
            _pipeline.VideoSource = videoSource;
    }

    public void SetTransformation(CameraSettings cameraSettings)
    {
        lock (_pipeline.SyncRoot)
        {
            if (_pipeline.ImageTransformer is ImageTransformer dualImageTransformer)
            {
                dualImageTransformer.Transformation = cameraSettings;
            }
        }
    }

    public async Task<bool> StartVideoSource(string cameraAddress, string preferredBackend)
    {
        if (string.IsNullOrEmpty(cameraAddress))
            return false;

        lock (_pipeline.SyncRoot)
        {
            if (_pipeline.VideoSource != null)
            {
                _pipeline.VideoSource.Dispose();
                _pipeline.VideoSource = null;
            }
        }

        SingleCameraSource cam;
        if (string.IsNullOrEmpty(preferredBackend))
            cam = await _singleCameraSourceFactory.CreateStart(cameraAddress);
        else
            cam = await _singleCameraSourceFactory.CreateStart(cameraAddress, preferredBackend);

        if (cam == null)
            return false;

        lock (_pipeline.SyncRoot)
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
        lock (_pipeline.SyncRoot)
            _pipeline.Filter = filter;
    }

    public static string GenerateMD5(string filepath)
    {
        using var stream = File.OpenRead(filepath);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "");
    }
}
