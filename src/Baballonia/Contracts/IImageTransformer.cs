using OpenCvSharp;

namespace Baballonia.Services.Inference;

public interface IImageTransformer
{
    Mat? Apply(Mat image);
}
