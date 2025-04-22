using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.Services.Inference.Enums;
using AvaloniaMiaDev.Services.Inference.Filters;
using AvaloniaMiaDev.Services.Inference.Models;
using AvaloniaMiaDev.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Threading;

namespace AvaloniaMiaDev.Services;

public class InferenceService : IInferenceService
{
    public (PlatformSettings, PlatformConnector)[] PlatformConnectors { get; }
        = new (PlatformSettings settings, PlatformConnector connector)[3];

    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly ILogger<InferenceService> _logger;
    private readonly ILocalSettingsService _localSettingsService;

    public InferenceService(ILogger<InferenceService> logger, ILocalSettingsService settingsService)
    {
        _logger = logger;
        _localSettingsService = settingsService;

        Task.Run(async () =>
        {
            logger.LogInformation("Starting Inference Service...");

            SessionOptions sessionOptions = SetupSessionOptions();
            await ConfigurePlatformSpecificGpu(sessionOptions);

            var minCutoff = await settingsService.ReadSettingAsync<float>("AppSettings_OneEuroMinFreqCutoff");
            var speedCoeff = await settingsService.ReadSettingAsync<float>("AppSettings_OneEuroSpeedCutoff");
            
            var eyeModel = await settingsService.ReadSettingAsync<string>("EyeHome_EyeModel");

            SetupInference(eyeModel, Camera.Left, minCutoff, speedCoeff, sessionOptions);
            SetupInference(eyeModel, Camera.Right, minCutoff, speedCoeff, sessionOptions);
            SetupInference("faceModel.onnx", Camera.Face, minCutoff, speedCoeff, sessionOptions);

            logger.LogInformation("Inference started!");
        });
    }

    /// <summary>
    /// Loads/reloads the ONNX model for a specified camera
    /// </summary>
    /// <param name="model"></param>
    /// <param name="camera"></param>
    /// <param name="minCutoff"></param>
    /// <param name="speedCoeff"></param>
    /// <param name="sessionOptions"></param>
    public void SetupInference(string model, Camera camera, float minCutoff, float speedCoeff, SessionOptions sessionOptions)
    {
        var modelName = model;
        var filter = new OneEuroFilter(
            minCutoff: minCutoff,
            beta: speedCoeff
        );

        var session = new InferenceSession(modelName, sessionOptions);
        var inputName = session.InputMetadata.Keys.First();
        var dimensions = session.InputMetadata.Values.First().Dimensions;
        var inputSize = new Size(dimensions[2], dimensions[3]);

        DenseTensor<float> tensor;
        if (camera is Camera.Left or Camera.Right)
        {
            // Handle the interleaved model
            tensor = new DenseTensor<float> ([1, 2, dimensions[2], dimensions[3]]);
        }
        else // Camera.Face
        {
            tensor = new DenseTensor<float> ([1, 1, dimensions[2], dimensions[3]]);
        }

        var platformSettings = new PlatformSettings(inputSize, session, tensor, filter, 0f, inputName, modelName);
        PlatformConnectors[(int)camera] = (platformSettings, null)!;
    }

    /// <summary>
    /// Poll expression data, frames
    /// </summary>
    /// <param name="camera"></param>
    /// <param name="cameraSettings"></param>
    /// <param name="arKitExpressions"></param>
    /// <returns></returns>
    public bool GetExpressionData(CameraSettings cameraSettings, out float[] arKitExpressions)
    {
        arKitExpressions = null!;

        var index = (int)cameraSettings.Camera;
        var platformSettings = PlatformConnectors[index].Item1;
        var platformConnector = PlatformConnectors[index].Item2;
        if (platformConnector is null)
        {
            return false;
        }

        if (platformConnector.Capture is null)
        {
            return false;
        }

        // Test if the camera is not ready or connecting to new source
        if (!platformConnector.Capture!.IsReady) return false;

        // Update the (256x256) image the onnx model uses
        if (platformConnector.ExtractFrameData(platformSettings.Tensor.Buffer.Span, platformSettings.InputSize, cameraSettings) != true)
            return false;

        // Camera ready, prepare Mat as DenseTensor
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(platformSettings.InputName, platformSettings.Tensor)
        };

        // Run inference!
        using var results = platformSettings.Session!.Run(inputs);
        arKitExpressions = results[0].AsEnumerable<float>().ToArray();
        float time = (float)_sw.Elapsed.TotalSeconds;
        platformSettings.Ms = (time - platformSettings.LastTime) * 1000;

        // if (cameraSettings.Camera == Camera.Face)
        // {
        //     _logger.LogInformation("Hello");
        // }

        // Filter ARKit Expressions
        for (int i = 0; i < arKitExpressions.Length; i++)
        {
            arKitExpressions[i] = platformSettings.Filter.Filter(arKitExpressions[i], time - platformSettings.LastTime);
        }

        // Log the filtered values for face tracking
        // if (cameraSettings.Camera == Camera.Face)
        // {
        //     _logger.LogInformation($"Face model FILTERED values: [{string.Join(", ", arKitExpressions.Select(v => v.ToString("F4")))}]");
        // }

        platformSettings.LastTime = time;
        return true;
    }

    /// <summary>
    /// Gets the pre-transform lip image for this frame
    /// This image will be (dimensions.width)px * (dimensions.height)px in provided ColorType
    /// </summary>
    /// <param name="color"></param>
    /// <param name="image"></param>
    /// <param name="dimensions"></param>
    /// <param name="cameraSettings"></param>
    /// <returns></returns>
    public bool GetRawImage(CameraSettings cameraSettings, ColorType color, out byte[] image, out (int width, int height) dimensions)
    {
        var index = (int)cameraSettings.Camera;
        var platformConnector = PlatformConnectors[index].Item2;
        if (platformConnector is null)
        {
            dimensions = (0, 0);
            image = [];
            return false;
        }

        if (platformConnector.Capture is null)
        {
            dimensions = (0, 0);
            image = [];
            return false;
        }

        if (!platformConnector.Capture.IsReady)
        {
            dimensions = (0, 0);
            image = [];
            return false;
        }

        if (platformConnector.Capture!.RawMat is null)
        {
            dimensions = (0, 0);
            image = [];
            return false;
        }

        if (platformConnector.Capture.Dimensions == (0, 0))
        {
            dimensions = (0, 0);
            image = [];
            return false;
        }

        dimensions = platformConnector.Capture!.Dimensions;
        if (color == (platformConnector.Capture!.RawMat.Channels() == 1 ? ColorType.Gray8 : ColorType.Bgr24))
        {
            image = platformConnector.Capture!.RawMat.AsSpan<byte>().ToArray();
        }
        else
        {
            using var convertedMat = new Mat();
            Cv2.CvtColor(platformConnector.Capture!.RawMat, convertedMat, (platformConnector.Capture!.RawMat.Channels() == 1) ? color switch
            {
                ColorType.Bgr24 => ColorConversionCodes.GRAY2BGR,
                ColorType.Rgb24 => ColorConversionCodes.GRAY2RGB,
                ColorType.Rgba32 => ColorConversionCodes.GRAY2RGBA,
            } : color switch
            {
                ColorType.Gray8 => ColorConversionCodes.BGR2GRAY,
                ColorType.Rgb24 => ColorConversionCodes.BGR2RGB,
                ColorType.Rgba32 => ColorConversionCodes.BGR2RGBA,
            });
            image = convertedMat.AsSpan<byte>().ToArray();
        }

        return true;
    }

    /// <summary>
    /// Gets the prost-transform lip image for this frame
    /// This image will be 256*256px, single-channel
    /// </summary>
    /// <param name="cameraSettings"></param>
    /// <param name="image"></param>
    /// <param name="dimensions"></param>
    /// <returns></returns>
    public bool GetImage(CameraSettings cameraSettings, out byte[]? image, out (int width, int height) dimensions)
    {
        image = null;
        dimensions = (0, 0);
        var platformSettings = PlatformConnectors[(int)cameraSettings.Camera].Item1;
        var platformConnector = PlatformConnectors[(int)cameraSettings.Camera].Item2;
        if (platformConnector is null) return false;

        byte[] data = new byte[platformSettings.InputSize.Width * platformSettings.InputSize.Height];
        using var imageMat = Mat<byte>.FromPixelData(platformSettings.InputSize.Height, platformSettings.InputSize.Width, data);
        if (platformConnector.TransformRawImage(imageMat, cameraSettings) != true) return false;

        image = data;
        dimensions = (imageMat.Width, imageMat.Height);
        return true;
    }

    /// <summary>
    /// Creates the proper video streaming classes based on the platform we're deploying to.
    /// EmguCV doesn't have support for VideoCapture on Android, iOS, or UWP
    /// We have a custom implementations for IP Cameras, the de-facto use case on mobile
    /// As well as SerialCameras (not tested on mobile yet)
    /// </summary>
    public void ConfigurePlatformConnectors(Camera camera, string cameraIndex)
    {
        if (OperatingSystem.IsAndroid())
        {
            PlatformConnectors[(int)camera].Item2 = new AndroidConnector(cameraIndex, _logger, _localSettingsService);
            PlatformConnectors[(int)camera].Item2.Initialize(cameraIndex);
        }
        else // if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Else, for WinUI, macOS, watchOS, MacCatalyst, tvOS, Tizen, etc...
            // Use the standard EmguCV VideoCapture backend
            PlatformConnectors[(int)camera].Item2 = new DesktopConnector(cameraIndex, _logger, _localSettingsService);
            PlatformConnectors[(int)camera].Item2.Initialize(cameraIndex);
        }
    }

    /// <summary>
    /// Make our SessionOptions *fancy*
    /// </summary>
    /// <returns></returns>
    public SessionOptions SetupSessionOptions()
    {
        // Random environment variable(s) to speed up webcam opening on the MSMF backend.
        // https://github.com/opencv/opencv/issues/17687
        Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");
        Environment.SetEnvironmentVariable("OMP_NUM_THREADS", "1");

        // Setup inference backend
        var sessionOptions = new SessionOptions();
        sessionOptions.InterOpNumThreads = 1;
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        // ~3% savings worth ~6ms avg latency. Not noticeable at 60fps?
        sessionOptions.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        sessionOptions.EnableMemoryPattern = true;
        return sessionOptions;
    }

    /// <summary>
    /// Per-platform hardware accel. detection/activation
    /// </summary>
    /// <param name="sessionOptions"></param>
    public async Task ConfigurePlatformSpecificGpu(SessionOptions sessionOptions)
    {
        sessionOptions.AppendExecutionProvider_CPU();

        var useGpu = await _localSettingsService.ReadSettingAsync<bool>("AppSettings_UseGPU");
        if (!useGpu) return;

        // "The Android Neural Networks API (NNAPI) is an Android C API designed for
        // running computationally intensive operations for machine learning on Android devices."
        // It was added in Android 8.1 and will be deprecated in Android 15
        if (OperatingSystem.IsAndroid() &&
            OperatingSystem.IsAndroidVersionAtLeast(8, 1) && // At least 8.1
            !OperatingSystem.IsAndroidVersionAtLeast(15))          // At most 15
        {
            sessionOptions.AppendExecutionProvider_Nnapi();
            _logger.LogInformation("Initialized ExecutionProvider: nnAPI");
            return;
        }

        if (OperatingSystem.IsIOS() ||
            OperatingSystem.IsMacCatalyst() ||
            OperatingSystem.IsMacOS() ||
            OperatingSystem.IsWatchOS() ||
            OperatingSystem.IsTvOS())
        {
            sessionOptions.AppendExecutionProvider_CoreML();
            _logger.LogInformation("Initialized ExecutionProvider: CoreML");
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            // If DirectML is supported on the user's system, try using it first.
            // This has support for both AMD and Nvidia GPUs, and uses less memory in my testing
            try
            {
                sessionOptions.AppendExecutionProvider_DML();
                _logger.LogInformation("Initialized ExecutionProvider: DirectML");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to configure Gpu.");
                _logger.LogWarning("Failed to create DML Execution Provider on Windows. Falling back to CUDA...");
            }
        }

        // If the user's system does not support DirectML (for whatever reason,
        // it's shipped with Windows 10, version 1903(10.0; Build 18362)+
        // Fallback on good ol' CUDA
        try
        {
            sessionOptions.AppendExecutionProvider_CUDA();
            _logger.LogInformation("Initialized ExecutionProvider: CUDA");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure Gpu.");
            _logger.LogWarning("Failed to create CUDA Execution Provider.");
        }

        // And, if CUDA fails (or we have an AMD card)
        // Try one more time with ROCm
        try
        {
            sessionOptions.AppendExecutionProvider_ROCm();
            _logger.LogInformation("Initialized ExecutionProvider: ROCm");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure ROCm.");
            _logger.LogWarning("Failed to create ROCm Execution Provider.");
        }

        _logger.LogWarning("No GPU acceleration will be applied.");
    }

    /// <summary>
    /// Shutdown and cleanup
    /// </summary>
    public void Shutdown(Camera camera)
    {
        var pc = PlatformConnectors[(int)camera].Item2;
        if (pc != null)
        {
            pc.Terminate();
        }
    }

    /// <summary>
    /// Shutdown and cleanup
    /// </summary>
    public void Shutdown()
    {
        foreach (var platformConnector in PlatformConnectors)
        {
            var pc = platformConnector.Item2;
            if (pc != null)
            {
                pc.Terminate();
            }
        }
    }
}
