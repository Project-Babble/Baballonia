using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;

namespace Baballonia.Services;

public abstract class InferenceService(ILogger<InferenceService> logger, ILocalSettingsService settingsService)
{
    public abstract (PlatformSettings, PlatformConnector)[] PlatformConnectors { get; }

    protected readonly ILogger<InferenceService> logger = logger;
    protected readonly ILocalSettingsService localSettingsService = settingsService;
    protected readonly Stopwatch sw = Stopwatch.StartNew();

    /// <summary>
    /// Loads/reloads the ONNX model for a specified camera
    /// </summary>
    /// <param name="model"></param>
    /// <param name="camera"></param>
    /// <param name="minCutoff"></param>
    /// <param name="speedCoeff"></param>
    /// <param name="sessionOptions"></param>
    public abstract void SetupInference(string model, Camera camera, float minCutoff, float speedCoeff,
        SessionOptions sessionOptions);

    /// <summary>
    /// Poll expression data, frames
    /// </summary>
    /// <param name="camera"></param>
    /// <param name="cameraSettings"></param>
    /// <param name="arKitExpressions"></param>
    /// <returns></returns>
    public abstract bool GetExpressionData(CameraSettings cameraSettings, out float[] arKitExpressions);

    /// <summary>
    /// Gets the pre-transform lip image for this frame
    /// This image will be (dimensions.width)px * (dimensions.height)px in provided ColorType
    /// </summary>
    /// <param name="color"></param>
    /// <param name="image"></param>
    /// <param name="dimensions"></param>
    /// <param name="cameraSettings"></param>
    /// <returns></returns>
    public abstract bool GetRawImage(CameraSettings cameraSettings, ColorType color, out Mat image,
        out (int width, int height) dimensions);

    /// <summary>
    /// Gets the prost-transform lip image for this frame
    /// This image will be 256*256px, single-channel
    /// </summary>
    /// <param name="cameraSettings"></param>
    /// <param name="image"></param>
    /// <param name="dimensions"></param>
    /// <returns></returns>
    public abstract bool GetImage(CameraSettings cameraSettings, out Mat? image, out (int width, int height) dimensions);

    /// <summary>
    /// Creates the proper video streaming classes based on the platform we're deploying to.
    /// OpenCVSharp doesn't have support for VideoCapture on Android, iOS, or UWP
    /// We have a custom implementations for IP Cameras, the de-facto use case on mobile
    /// As well as SerialCameras (not tested on mobile yet)
    /// </summary>
    public void ConfigurePlatformConnectors(Camera camera, string cameraIndex)
    {
        if (OperatingSystem.IsAndroid())
        {
            PlatformConnectors[(int)camera].Item2 = new AndroidConnector(cameraIndex, logger, localSettingsService);
            PlatformConnectors[(int)camera].Item2.Initialize(cameraIndex);
        }
        else // if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Else, for WinUI, macOS, watchOS, MacCatalyst, tvOS, Tizen, etc...
            // Use the standard OpenCVSharp VideoCapture backend
            PlatformConnectors[(int)camera].Item2 = new DesktopConnector(cameraIndex, logger, localSettingsService);
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
        var useGpu = await localSettingsService.ReadSettingAsync<bool>("AppSettings_UseGPU");
        if (!useGpu)
        {
            sessionOptions.AppendExecutionProvider_CPU();
            return;
        }

        // "The Android Neural Networks API (NNAPI) is an Android C API designed for
        // running computationally intensive operations for machine learning on Android devices."
        // It was added in Android 8.1 and will be deprecated in Android 15
        if (OperatingSystem.IsAndroid() &&
            OperatingSystem.IsAndroidVersionAtLeast(8, 1) && // At least 8.1
            !OperatingSystem.IsAndroidVersionAtLeast(15))          // At most 15
        {
            sessionOptions.AppendExecutionProvider_Nnapi();
            logger.LogInformation("Initialized ExecutionProvider: nnAPI");
            return;
        }

        if (OperatingSystem.IsIOS() ||
            OperatingSystem.IsMacCatalyst() ||
            OperatingSystem.IsMacOS() ||
            OperatingSystem.IsWatchOS() ||
            OperatingSystem.IsTvOS())
        {
            sessionOptions.AppendExecutionProvider_CoreML();
            logger.LogInformation("Initialized ExecutionProvider: CoreML");
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            // If DirectML is supported on the user's system, try using it first.
            // This has support for both AMD and Nvidia GPUs, and uses less memory in my testing
            try
            {
                sessionOptions.AppendExecutionProvider_DML();
                logger.LogInformation("Initialized ExecutionProvider: DirectML");
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to configure Gpu.");
                logger.LogWarning("Failed to create DML Execution Provider on Windows. Falling back to CUDA...");
            }
        }

        // If the user's system does not support DirectML (for whatever reason,
        // it's shipped with Windows 10, version 1903(10.0; Build 18362)+
        // Fallback on good ol' CUDA
        try
        {
            sessionOptions.AppendExecutionProvider_CUDA();
            logger.LogInformation("Initialized ExecutionProvider: CUDA");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure Gpu.");
            logger.LogWarning("Failed to create CUDA Execution Provider.");
        }

        // And, if CUDA fails (or we have an AMD card)
        // Try one more time with ROCm
        try
        {
            sessionOptions.AppendExecutionProvider_ROCm();
            logger.LogInformation("Initialized ExecutionProvider: ROCm");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure ROCm.");
            logger.LogWarning("Failed to create ROCm Execution Provider.");
        }

        logger.LogWarning("No GPU acceleration will be applied.");
        sessionOptions.AppendExecutionProvider_CPU();
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
