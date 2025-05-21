using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        = new (PlatformSettings settings, PlatformConnector connector)[3];

    private int[] _combinedDimensions;
    private DenseTensor<float> _combinedTensor;
    private byte[] _matBytes = [];

    // Queue for each camera to hold incoming frames
    private readonly ConcurrentQueue<FrameData> _frameQueues = new();

    // Minimum number of frames required before processing
    private const int FramesForInference = 4;

    public EyeInferenceService(ILogger<InferenceService> logger, ILocalSettingsService settingsService)
        : base(logger, settingsService)
    {
        Task.Run(async () =>
        {
            logger.LogInformation("Starting Eye Inference Service...");

            SessionOptions sessionOptions = SetupSessionOptions();
            await ConfigurePlatformSpecificGpu(sessionOptions);

            var minCutoff = await settingsService.ReadSettingAsync<float>("AppSettings_OneEuroMinFreqCutoff");
            var speedCoeff = await settingsService.ReadSettingAsync<float>("AppSettings_OneEuroSpeedCutoff");
            var eyeModel = await settingsService.ReadSettingAsync<string>("EyeHome_EyeModel");

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
    public override void SetupInference(string model, Camera camera, float minCutoff, float speedCoeff,
        SessionOptions sessionOptions)
    {
        var filter = new OneEuroFilter(
            minCutoff: minCutoff,
            beta: speedCoeff
        );

        var session = new InferenceSession(Path.Combine(AppContext.BaseDirectory, model), sessionOptions);
        var inputName = session.InputMetadata.Keys.First();
        var dimensions = session.InputMetadata.Values.First().Dimensions;
        var inputSize = new Size(dimensions[2], dimensions[3]);
        var magicAiNumberSize = 4;

        DenseTensor<float> tensor = new DenseTensor<float>([1, magicAiNumberSize, dimensions[2], dimensions[3]]);

        // This is set up twice but I do not care
        _combinedDimensions = [1, magicAiNumberSize * 2, dimensions[2], dimensions[3]];
        _combinedTensor = new DenseTensor<float>(_combinedDimensions);

        var platformSettings = new PlatformSettings(inputSize, session, tensor, filter, 0f, inputName, model);
        PlatformConnectors[(int)camera] = (platformSettings, null)!;
    }

    /// <summary>
    /// Captures a frame and adds it to the queue for later processing
    /// </summary>
    /// <param name="cameraSettings"></param>
    /// <returns>True if frame was successfully captured and added to queue</returns>
    private bool CaptureFrame(CameraSettings cameraSettings)
    {
        if (PlatformConnectors[(int)Camera.Left].Item2 is null ||
            PlatformConnectors[(int)Camera.Left].Item2.Capture is null ||
            !PlatformConnectors[(int)Camera.Left].Item2.Capture!.IsReady ||
            PlatformConnectors[(int)Camera.Right].Item2 is null ||
            PlatformConnectors[(int)Camera.Right].Item2.Capture is null ||
            !PlatformConnectors[(int)Camera.Right].Item2.Capture!.IsReady)
        {
            return false;
        }

        Mat matLeft = new Mat<byte>(PlatformConnectors[(int)Camera.Left].Item1.InputSize.Height,
            PlatformConnectors[(int)Camera.Left].Item1.InputSize.Width);
        PlatformConnectors[(int)Camera.Left].Item2.TransformRawImage(matLeft, cameraSettings);
        if (matLeft.Empty()) return false;

        Mat matRight = new Mat<byte>(PlatformConnectors[(int)Camera.Right].Item1.InputSize.Height,
            PlatformConnectors[(int)Camera.Right].Item1.InputSize.Width);
        PlatformConnectors[(int)Camera.Right].Item2.TransformRawImage(matRight, cameraSettings);
        if (matRight.Empty()) return false;

        Mat matCombined = new Mat();
        Cv2.Merge([matLeft, matRight], matCombined);

        var frameDataCombined = new FrameData
        {
            CameraSettings = cameraSettings,
            Timestamp = sw.Elapsed.TotalSeconds,
            Mat = matCombined,
        };

        if (_frameQueues.Count > FramesForInference)
        {
            _frameQueues.TryDequeue(out _);
        }

        _frameQueues.Enqueue(frameDataCombined);

        return true;
    }

    /// <summary>
    /// Poll expression data, frames
    /// </summary>
    /// <param name="cameraSettings"></param>
    /// <param name="arKitExpressions"></param>
    /// <returns></returns>
    public override bool GetExpressionData(CameraSettings cameraSettings, out float[] arKitExpressions)
    {
        arKitExpressions = null!;

        if (PlatformConnectors[(int)Camera.Left].Item2 is null || PlatformConnectors[(int)Camera.Left].Item2.Capture is null || !PlatformConnectors[(int)Camera.Left].Item2.Capture!.IsReady ||
            PlatformConnectors[(int)Camera.Right].Item2 is null || PlatformConnectors[(int)Camera.Right].Item2.Capture is null || !PlatformConnectors[(int)Camera.Right].Item2.Capture!.IsReady)
        {
            return false;
        }

        // First capture the current frame
        if (!CaptureFrame(cameraSettings))
        {
            return false;
        }

        // Check if we have enough frames in the queue
        if (_frameQueues.Count < FramesForInference)
        {
            return false;
        }

        // Pop old frames until we have FramesForInference
        while (_frameQueues.Count != FramesForInference)
        {
            _frameQueues.TryDequeue(out _);
        }

        // This will update _combinedTensor
        ConvertMatsArrayToDenseTensor(_frameQueues.Select(fd => fd.Mat).ToArray());

        // Run inference on the dequeued frame's tensor
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(PlatformConnectors[(int)Camera.Left].Item1.InputName, _combinedTensor)
        };

        // Run inference!
        using var results = PlatformConnectors[(int)Camera.Left].Item1.Session!.Run(inputs);
        arKitExpressions = results[0].AsEnumerable<float>().ToArray();

        float time = (float)sw.Elapsed.TotalSeconds;
        var delta = time - PlatformConnectors[(int)Camera.Left].Item1.LastTime;
        PlatformConnectors[(int)Camera.Left].Item1.Ms = delta * 1000f;
        PlatformConnectors[(int)Camera.Right].Item1.Ms = delta * 1000f;

        // Filter ARKit Expressions. This is broken rn!
        /*for (int i = 0; i < arKitExpressions.Length; i++)
        {
            arKitExpressions[i] = PlatformConnectors[(int)Camera.Left].Item1.Filter.Filter(arKitExpressions[i], delta);
        }*/

        PlatformConnectors[(int)Camera.Left].Item1.LastTime = time;
        PlatformConnectors[(int)Camera.Right].Item1.LastTime = time;
        return true;
    }

    private void ConvertMatsArrayToDenseTensor(Mat[] mats)
    {
        // Verify the input array has exactly 4 mats
        if (mats.Length != 4)
        {
            throw new ArgumentException($"Expected 4 mats, but got: {mats.Length}");
        }

        // Verify each mat is CV_8UC2
        foreach (var mat in mats)
        {
            if (mat.Type() != MatType.CV_8UC2)
            {
                throw new ArgumentException($"Expected CV_8UC2 mat, but got: {mat.Type()}");
            }
        }

        // Ensure all mats have the same dimensions
        int width = mats[0].Width;
        int height = mats[0].Height;

        foreach (var mat in mats)
        {
            if (mat.Width != width || mat.Height != height)
            {
                throw new ArgumentException("All mats must have the same dimensions");
            }
        }

        // Create new tensor with specified dimensions
        // The tensor will have shape [batchSize, channels, height, width]
        // where channels = 2 channels per mat * 4 mats = 8 total channels

        int batchSize = _combinedDimensions[0];
        int tensorChannels = _combinedDimensions[1];

        // We expect tensorChannels to be 8 (2 channels * 4 mats)
        if (tensorChannels != 8)
        {
            throw new ArgumentException($"Tensor channels dimension should be 8 (2 channels * 4 mats), but got: {tensorChannels}");
        }

        // Process each mat
        for (int matIndex = 0; matIndex < mats.Length; matIndex++)
        {
            Mat continuousMat = mats[matIndex].IsContinuous() ? mats[matIndex] : mats[matIndex].Clone();

            // Get the raw data bytes from the Mat
            var size = continuousMat.Total() * continuousMat.ElemSize();
            if (_matBytes.Length != size)
                Array.Resize(ref _matBytes, (int)size);

            Marshal.Copy(continuousMat.Data, _matBytes, 0, _matBytes.Length);

            // Process each pixel's two channels
            for (int b = 0; b < batchSize; b++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Get the byte offset for the current pixel in the Mat
                        int matOffset = (y * width + x) * 2; // 2 channels per pixel

                        float normalizedValue0 = _matBytes[matOffset] / 255.0f;
                        float normalizedValue1 = _matBytes[matOffset + 1] / 255.0f;

                        _combinedTensor[b, matIndex * 2, y, x] = normalizedValue1;
                        _combinedTensor[b, matIndex * 2 + 1, y, x] = normalizedValue0;
                    }
                }
            }
        }
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
    /// Class to store frame data in the queue
    /// </summary>
    private class FrameData
    {
        public CameraSettings CameraSettings { get; set; }
        public Mat Mat { get; init; }
        public double Timestamp { get; set; }
    }
}
