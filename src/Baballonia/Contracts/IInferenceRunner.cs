using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Baballonia.Contracts;

public interface IInferenceRunner
{
    public OrderedFloatMap? Run();
    public DenseTensor<float> GetInputTensor();
}
