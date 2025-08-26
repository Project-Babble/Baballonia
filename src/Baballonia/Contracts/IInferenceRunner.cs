using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Baballonia.Contracts;

public interface IInferenceRunner
{
    public float[]? Run();
    public DenseTensor<float> GetInputTensor();
}
