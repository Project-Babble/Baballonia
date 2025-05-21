using System;
using System.Collections.Generic;
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

public class FaceInferenceService : InferenceService, IFaceInferenceService
{
    public override (PlatformSettings, PlatformConnector)[] PlatformConnectors { get; }
        = new (PlatformSettings settings, PlatformConnector connector)[1];

    public FaceInferenceService(ILogger<InferenceService> logger, ILocalSettingsService settingsService)
        : base(logger, settingsService)
    {
        Task.Run(async () =>
        {
            logger.LogInformation("Starting Face Inference Service...");

            SessionOptions sessionOptions = SetupSessionOptions();
            await ConfigurePlatformSpecificGpu(sessionOptions);

            var minCutoff = await settingsService.ReadSettingAsync<float>("AppSettings_OneEuroMinFreqCutoff");
            var speedCoeff = await settingsService.ReadSettingAsync<float>("AppSettings_OneEuroSpeedCutoff");

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

        DenseTensor<float> tensor = new DenseTensor<float> ([1, 1, dimensions[2], dimensions[3]]);

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
    public override bool GetExpressionData(CameraSettings cameraSettings, out float[] arKitExpressions)
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

        float time = (float)sw.Elapsed.TotalSeconds;
        var delta = time - platformSettings.LastTime;
        platformSettings.Ms = delta * 1000;

        // Filter ARKit Expressions.
        for (int i = 0; i < arKitExpressions.Length; i++)
        {
            arKitExpressions[i] = platformSettings.Filter.Filter(arKitExpressions[i], delta);
        }

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
    /// Gets the prost-transform lip image for this frame
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
        var platformSettings = PlatformConnectors[0].Item1;
        var platformConnector = PlatformConnectors[0].Item2;
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
}
