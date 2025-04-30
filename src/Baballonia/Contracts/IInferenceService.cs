using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace Baballonia.Contracts;

public interface IInferenceService
{
    public (PlatformSettings, PlatformConnector)[] PlatformConnectors { get; }
    public bool GetExpressionData(CameraSettings cameraSettings, out float[] arKitExpressions);

    public bool GetRawImage(CameraSettings cameraSettings, ColorType color, out Mat image, out (int width, int height) dimensions);

    public bool GetImage(CameraSettings cameraSettings, out Mat? image, out (int width, int height) dimensions);

    public void ConfigurePlatformConnectors(Camera camera, string cameraIndex);

    public void SetupInference(string model, Camera camera, float minCutoff, float speedCoeff, SessionOptions sessionOptions);
    public SessionOptions SetupSessionOptions();
    public Task ConfigurePlatformSpecificGpu(SessionOptions sessionOptions);

    public void Shutdown(Camera camera);
    public void Shutdown();
}
