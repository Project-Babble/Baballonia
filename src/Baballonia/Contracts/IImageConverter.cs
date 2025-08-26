using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Baballonia.Services.Inference;

public interface IImageConverter
{
    void Convert(Mat input, DenseTensor<float> outTensor);
}
