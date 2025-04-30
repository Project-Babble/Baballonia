using System;
using Baballonia.Services.Inference.Filters;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Baballonia.Services.Inference.Platforms;

public class PlatformSettings(
    Size inputSize,
    InferenceSession session,
    DenseTensor<float> tensor,
    OneEuroFilter oneEuroFilter,
    float lastTime,
    string inputName,
    string modelName)
{
    public Size InputSize { get; } = inputSize;
    public InferenceSession Session { get; } = session;
    public DenseTensor<float> Tensor { get; } = tensor;

    public OneEuroFilter Filter { get; } = oneEuroFilter;
    public float LastTime { get; set; } = lastTime;
    public string InputName { get; } = inputName;
    public string ModelName { get; } = modelName;
    public float Ms { get; set;  }
    public int Fps => (int) MathF.Floor(1000f / Ms);
}
