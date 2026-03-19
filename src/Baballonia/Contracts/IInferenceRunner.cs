using Microsoft.ML.OnnxRuntime.Tensors;

namespace Baballonia.Contracts;

public interface IInferenceRunner
{
    public float[]? Run();
    public DenseTensor<float> GetInputTensor();
}
