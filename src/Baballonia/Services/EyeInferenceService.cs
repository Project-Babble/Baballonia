using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Filters;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;

namespace Baballonia.Services;

public class EyeInferenceService : InferenceService, IEyeInferenceService
{
    public override (PlatformSettings, PlatformConnector)[] PlatformConnectors { get; }
        = new (PlatformSettings settings, PlatformConnector connector)[2];

    // Queue for each camera to hold incoming frames
    private readonly ConcurrentQueue<FrameData>[] _frameQueues = new ConcurrentQueue<FrameData>[3];

    // Minimum number of frames required before processing
    private const int MinFramesForInference = 3;

    public EyeInferenceService(ILogger<InferenceService> logger, ILocalSettingsService settingsService)
        : base(logger, settingsService)
    {
        // Initialize frame queues for each camera
        for (int i = 0; i < _frameQueues.Length; i++)
        {
            _frameQueues[i] = new ConcurrentQueue<FrameData>();
        }

        Task.Run(async () =>
        {
            logger.LogInformation("Starting Eye Inference Service...");

            SessionOptions sessionOptions = SetupSessionOptions();
            await ConfigurePlatformSpecificGpu(sessionOptions);

            var minCutoff = await settingsService.ReadSettingAsync<float>("AppSettings_OneEuroMinFreqCutoff");
            var speedCoeff = await settingsService.ReadSettingAsync<float>("AppSettings_OneEuroSpeedCutoff");

            var eyeModel = await settingsService.ReadSettingAsync<string>("EyeHome_EyeModel") ?? "eyeModel.onnx";

            SetupInference(eyeModel, Camera.Left, minCutoff, speedCoeff, sessionOptions);
            SetupInference(eyeModel, Camera.Right, minCutoff, speedCoeff, sessionOptions);

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
    public override void SetupInference(string model, Camera camera, float minCutoff, float speedCoeff, SessionOptions sessionOptions)
    {
        var modelName = model;
        var filter = new OneEuroFilter(
            minCutoff: minCutoff,
            beta: speedCoeff
        );

        var session = new InferenceSession(Path.Combine(AppContext.BaseDirectory, modelName), sessionOptions);
        var inputName = session.InputMetadata.Keys.First();
        var dimensions = session.InputMetadata.Values.First().Dimensions;
        var inputSize = new Size(dimensions[2], dimensions[3]);

        DenseTensor<float> tensor = new DenseTensor<float>([1, 8, dimensions[2], dimensions[3]]);

        var platformSettings = new PlatformSettings(inputSize, session, tensor, filter, 0f, inputName, modelName);
        PlatformConnectors[(int)camera] = (platformSettings, null)!;
    }

    /// <summary>
    /// Captures a frame and adds it to the queue for later processing
    /// </summary>
    /// <param name="cameraSettings"></param>
    /// <returns>True if frame was successfully captured and added to queue</returns>
    public bool CaptureFrame(CameraSettings cameraSettings)
    {
        var index = (int)cameraSettings.Camera;
        var platformSettings = PlatformConnectors[index].Item1;
        var platformConnector = PlatformConnectors[index].Item2;

        if (platformConnector?.Capture is null || !platformConnector.Capture.IsReady)
        {
            return false;
        }

        var frameData = new FrameData
        {
            CameraSettings = cameraSettings,
            Timestamp = sw.Elapsed.TotalSeconds
        };

        // Create a copy of tensor for this frame
        frameData.Tensor = new DenseTensor<float>([1, 8, platformSettings.InputSize.Height, platformSettings.InputSize.Width]);

        // Extract frame data to the tensor
        if (!platformConnector.ExtractFrameData(frameData.Tensor.Buffer.Span, platformSettings.InputSize, cameraSettings))
        {
            return false;
        }

        // Add frame to the queue
        _frameQueues[index].Enqueue(frameData);

        return true;
    }

    /// <summary>
    /// Poll expression data, frames
    /// </summary>
    /// <param name="camera"></param>
    /// <param name="cameraSettings"></param>
    /// <param name="arKitExpressions"></param>
    /// <returns></returns>
    public override bool GetExpressionData(CameraSettings cameraSettings, out float[] arKitExpressions)
    {
        arKitExpressions = null!;

        var index = (int)cameraSettings.Camera;
        var platformSettings = PlatformConnectors[index].Item1;
        var platformConnector = PlatformConnectors[index].Item2;

        if (platformConnector is null || platformConnector.Capture is null || !platformConnector.Capture.IsReady)
        {
            return false;
        }

        // First capture the current frame
        if (!CaptureFrame(cameraSettings))
        {
            return false;
        }

        // Check if we have enough frames in the queue
        if (_frameQueues[index].Count < MinFramesForInference)
        {
            logger.LogDebug($"Not enough frames in queue for camera {cameraSettings.Camera}. Current count: {_frameQueues[index].Count}");
            return false;
        }

        // Process the oldest frame in the queue
        if (!_frameQueues[index].TryDequeue(out var frameData))
        {
            return false;
        }

        // Run inference on the dequeued frame's tensor
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(platformSettings.InputName, frameData.Tensor)
        };

        // Run inference!
        using var results = platformSettings.Session!.Run(inputs);
        arKitExpressions = results[0].AsEnumerable<float>().ToArray();

        float currentTime = (float)sw.Elapsed.TotalSeconds;
        float delta = currentTime - platformSettings.LastTime;
        platformSettings.Ms = delta * 1000;

        // Filter ARKit Expressions. This is broken rn!
        //for (int i = 0; i < arKitExpressions.Length; i++)
        //{
        //    arKitExpressions[i] = platformSettings.Filter.Filter(arKitExpressions[i], delta);
        //}

        platformSettings.LastTime = currentTime;
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
    public override bool GetRawImage(CameraSettings cameraSettings, ColorType color, out Mat image, out (int width, int height) dimensions)
    {
        var index = (int)cameraSettings.Camera;
        var platformConnector = PlatformConnectors[index].Item2;
        dimensions = (0, 0);
        image = new Mat();

        if (platformConnector is null)
            return false;

        if (platformConnector.Capture is null)
            return false;

        if (!platformConnector.Capture.IsReady)
            return false;

        if (platformConnector.Capture.RawMat is null)
            return false;

        if (platformConnector.Capture.Dimensions == (0, 0))
            return false;

        dimensions = platformConnector.Capture!.Dimensions;
        if (color == (platformConnector.Capture!.RawMat.Channels() == 1 ? ColorType.Gray8 : ColorType.Bgr24))
        {
            image = platformConnector.Capture!.RawMat;
        }
        else
        {
            var convertedMat = new Mat();
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
            image = convertedMat;
        }

        return true;
    }

    /// <summary>
    /// Gets the post-transform lip image for this frame
    /// This image will be 256*256px, single-channel
    /// </summary>
    /// <param name="cameraSettings"></param>
    /// <param name="image"></param>
    /// <param name="dimensions"></param>
    /// <returns></returns>
    public override bool GetImage(CameraSettings cameraSettings, out Mat? image, out (int width, int height) dimensions)
    {
        image = null;
        dimensions = (0, 0);
        var platformSettings = PlatformConnectors[(int)cameraSettings.Camera].Item1;
        var platformConnector = PlatformConnectors[(int)cameraSettings.Camera].Item2;
        if (platformConnector is null) return false;

        var imageMat = new Mat<byte>(platformSettings.InputSize.Height, platformSettings.InputSize.Width);

        if (platformConnector.TransformRawImage(imageMat, cameraSettings) != true)
        {
            imageMat.Dispose();
            return false;
        }

        image = imageMat;
        dimensions = (imageMat.Width, imageMat.Height);
        return true;
    }

    /// <summary>
    /// Clear all frame queues, useful when changing camera sources or resetting
    /// </summary>
    public void ClearFrameQueues()
    {
        for (int i = 0; i < _frameQueues.Length; i++)
        {
            while (_frameQueues[i].TryDequeue(out _)) { }
        }
    }

    /// <summary>
    /// Class to store frame data in the queue
    /// </summary>
    private class FrameData
    {
        public CameraSettings CameraSettings { get; set; }
        public DenseTensor<float> Tensor { get; set; }
        public double Timestamp { get; set; }
    }
}
