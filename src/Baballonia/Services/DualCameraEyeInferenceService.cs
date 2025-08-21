using Baballonia.Contracts;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Filters;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Baballonia.Services;

/// <summary>
/// Implementation of IEyeInferenceService that uses SEPARATE cameras for each eye
/// </summary>
public class DualCameraEyeInferenceService(ILogger<InferenceService> logger, ILocalSettingsService settingsService) : BaseEyeInferenceService(logger, settingsService), IDualCameraEyeInferenceService
{
    private readonly Dictionary<Camera, string> _cameraUrls = new();
    private readonly (PlatformSettings, PlatformConnector)[] _platformConnectors = new (PlatformSettings, PlatformConnector)[3];

    private readonly ILogger<InferenceService> _logger = logger;
    private readonly ILocalSettingsService _settingsService = settingsService;

    public override EyeInferenceType Type => EyeInferenceType.DualCamera;
    public override IReadOnlyDictionary<Camera, string> CameraUrls => _cameraUrls;
    public override (PlatformSettings, PlatformConnector)[] PlatformConnectors => _platformConnectors;

    private bool _useFilter = true;

    public override void SetupInference(Camera camera, string cameraAddress)
    {
        Task.Run(async () =>
        {
            _cameraUrls[camera] = cameraAddress;

            if (_cameraUrls.Count >= 2)
            {
                if (camera == Camera.Left)
                {
                    _logger.LogInformation($"Setting up DualCamera inference for camera {cameraAddress}");
                    await InitializeModel();
                }
            }
        });
    }

    protected override async Task InitializeModel()
    {
        SessionOptions sessionOptions = SetupSessionOptions();
        await ConfigurePlatformSpecificGpu(sessionOptions);

        _useFilter = await _settingsService.ReadSettingAsync<bool>("AppSettings_OneEuroMinEnabled");
        var minCutoff = await _settingsService.ReadSettingAsync<float>("AppSettings_OneEuroMinFreqCutoff");
        if (minCutoff == 0f) minCutoff = 1f;
        var speedCoeff = await _settingsService.ReadSettingAsync<float>("AppSettings_OneEuroSpeedCutoff");
        if (speedCoeff == 0f) speedCoeff = 1f;
        var eyeModel = await _settingsService.ReadSettingAsync<string>("EyeHome_EyeModel");

        if (!File.Exists(eyeModel))
        {
            const string defaultModelName = "eyeModel.onnx";
            await _settingsService.SaveSettingAsync<string>("EyeHome_EyeModel", defaultModelName);
            eyeModel = defaultModelName;
        }

        var session = new InferenceSession(Path.Combine(AppContext.BaseDirectory, eyeModel), sessionOptions);
        var inputName = session.InputMetadata.Keys.First();
        var dimensions = session.InputMetadata.Values.First().Dimensions;
        var inputSize = new Size(dimensions[2], dimensions[3]);

        // Initialize tensors and filters for both eyes
        for (int i = 0; i < 2; i++)
        {
            float[] noisyPoint = new float[ExpectedRawExpressions];
            var filter = new OneEuroFilter(
                x0: noisyPoint,
                minCutoff: minCutoff,
                beta: speedCoeff
            );

            var tensor = new DenseTensor<float>([1, 4, dimensions[2], dimensions[3]]);
            var platformSettings = new PlatformSettings(inputSize, session, tensor, filter, 0f, inputName, eyeModel);
            _platformConnectors[i] = (platformSettings, null)!;
        }

        _combinedDimensions = [1, 8, dimensions[2], dimensions[3]]; // 2 eyes * 4 frames
        _combinedTensor = new DenseTensor<float>(_combinedDimensions);

        // Configure platform connectors for both cameras
        ConfigurePlatformConnectors(Camera.Left, _cameraUrls[Camera.Left]);
        ConfigurePlatformConnectors(Camera.Right, _cameraUrls[Camera.Right]);

        _logger.LogInformation($"{Type} inference service initialized with separate cameras");
    }

    public override bool GetExpressionData(CameraSettings cameraSettings, out float[] arKitExpressions)
    {
        arKitExpressions = null!;

        // For dual camera, we only process when we have both camera feeds
        if (PlatformConnectors[(int)Camera.Left].Item2?.Capture?.IsReady != true ||
            PlatformConnectors[(int)Camera.Right].Item2?.Capture?.IsReady != true)
        {
            return false;
        }

        using var leftEyeMat = new Mat<byte>(
            PlatformConnectors[(int)Camera.Left].Item1.InputSize.Height,
            PlatformConnectors[(int)Camera.Left].Item1.InputSize.Width);

        using var rightEyeMat = new Mat<byte>(
            PlatformConnectors[(int)Camera.Right].Item1.InputSize.Height,
            PlatformConnectors[(int)Camera.Right].Item1.InputSize.Width);

        // Capture frame from both cameras
        if (!CaptureFrame(cameraSettings, leftEyeMat, rightEyeMat))
        {
            return false;
        }

        // Check if we have enough frames in the queue
        if (_frameQueues.Count < FramesForInference)
        {
            return false;
        }

        // Pop old frames until we have exactly FramesForInference
        while (_frameQueues.Count > FramesForInference)
        {
            _frameQueues.TryDequeue(out _);
        }

        // Convert queued frames to tensor
        ConvertMatsArrayToDenseTensor(_frameQueues.Select(fd => fd.Mat).ToArray());

        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(PlatformConnectors[(int)Camera.Left].Item1.InputName, _combinedTensor)
        };

        using var results = PlatformConnectors[(int)Camera.Left].Item1.Session!.Run(inputs);
        arKitExpressions = results[0].AsEnumerable<float>().ToArray();

        // Apply filter
        if (_useFilter)
            arKitExpressions = PlatformConnectors[(int)Camera.Left].Item1.Filter.Filter(arKitExpressions);

        // Process and convert the expressions to the expected format
        return ProcessExpressions(ref arKitExpressions); ;
    }

    private bool ProcessExpressions(ref float[] arKitExpressions)
    {
        if (arKitExpressions.Length < ExpectedRawExpressions)
            return false;

        const float mulV = 2.0f;
        const float mulY = 2.0f;

        var leftPitch = arKitExpressions[0] * mulY - mulY / 2;
        var leftYaw = arKitExpressions[1] * mulV - mulV / 2;
        var leftLid = 1 - arKitExpressions[2];

        var rightPitch = arKitExpressions[3] * mulY - mulY / 2;
        var rightYaw = arKitExpressions[4] * mulV - mulV / 2;
        var rightLid = 1 - arKitExpressions[5];

        var eyeY = (leftPitch * leftLid + rightPitch * rightLid) / (leftLid + rightLid);

        var leftEyeYawCorrected = rightYaw * (1 - leftLid) + leftYaw * leftLid;
        var rightEyeYawCorrected = leftYaw * (1 - rightLid) + rightYaw * rightLid;

        // [left yaw, left pitch, left lid...
        float[] convertedExpressions = new float[ExpectedRawExpressions];

        // swap eyes at this point
        convertedExpressions[0] = rightEyeYawCorrected; // left yaw (LeftEyeX)
        convertedExpressions[1] = eyeY;                 // left pitch (LeftEyeY)
        convertedExpressions[2] = rightLid;             // left lid
        convertedExpressions[3] = leftEyeYawCorrected;  // right yaw (RightEyeX)
        convertedExpressions[4] = eyeY;                 // right pitch (RightEyeY)
        convertedExpressions[5] = leftLid;              // right lid

        arKitExpressions = convertedExpressions;

        float time = (float)sw.Elapsed.TotalSeconds;
        var delta = time - PlatformConnectors[(int)Camera.Left].Item1.LastTime;
        PlatformConnectors[(int)Camera.Left].Item1.Ms = delta * 1000f;
        PlatformConnectors[(int)Camera.Right].Item1.Ms = delta * 1000f;

        PlatformConnectors[(int)Camera.Left].Item1.LastTime = time;
        PlatformConnectors[(int)Camera.Right].Item1.LastTime = time;

        return true;
    }

    public override bool GetRawImage(CameraSettings cameraSettings, ColorType color, out Mat image)
    {
        var index = (int)cameraSettings.Camera;
        var platformConnector = PlatformConnectors[index].Item2;
        image = new Mat();

        if (platformConnector?.Capture?.RawMat == null || !platformConnector.Capture.IsReady)
            return false;

        if (color == (platformConnector.Capture.RawMat.Channels() == 1 ? ColorType.Gray8 : ColorType.Bgr24))
        {
            image = platformConnector.Capture.RawMat;
        }
        else
        {
            var convertedMat = new Mat();
            Cv2.CvtColor(platformConnector.Capture.RawMat, convertedMat,
                platformConnector.Capture.RawMat.Channels() == 1
                    ? color switch
                    {
                        ColorType.Bgr24 => ColorConversionCodes.GRAY2BGR,
                        ColorType.Rgb24 => ColorConversionCodes.GRAY2RGB,
                        ColorType.Rgba32 => ColorConversionCodes.GRAY2RGBA,
                        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
                    }
                    : color switch
                    {
                        ColorType.Gray8 => ColorConversionCodes.BGR2GRAY,
                        ColorType.Rgb24 => ColorConversionCodes.BGR2RGB,
                        ColorType.Rgba32 => ColorConversionCodes.BGR2RGBA,
                        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null)
                    });
            image = convertedMat;
        }

        return true;
    }

    public override bool GetImage(CameraSettings cameraSettings, out Mat? image)
    {
        image = null;
        var platformSettings = PlatformConnectors[(int)cameraSettings.Camera].Item1;
        var platformConnector = PlatformConnectors[(int)cameraSettings.Camera].Item2;
        if (platformConnector is null)
            return false;

        var imageMat = new Mat<byte>(platformSettings.InputSize.Height, platformSettings.InputSize.Width);

        if (platformConnector.TransformRawImage(imageMat, cameraSettings) != true)
        {
            imageMat.Dispose();
            return false;
        }

        image = imageMat;
        return true;
    }
}
