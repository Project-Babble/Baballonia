using System;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace Baballonia.Services.Inference;

public class MatToFloatTensorConverter : IImageConverter
{
    public void Convert(Mat input, DenseTensor<float> outTensor)
    {
        Mat resultMat;
        if (input.Type() != MatType.CV_32FC(input.Channels()))
        {
            resultMat = new Mat();
            input.ConvertTo(resultMat, MatType.CV_32FC(input.Channels()), 1f / 255f);
        }
        else
        {
            resultMat = input;
        }
        Cv2.Resize(resultMat, resultMat, new Size(outTensor.Dimensions[2], outTensor.Dimensions[3]));
        if (!resultMat.IsContinuous())
        {
            resultMat = resultMat.Clone(); // Make it continuous
        }

        int height = resultMat.Rows;
        int width = resultMat.Cols;
        int channels = resultMat.Channels();

        IntPtr matPtr = resultMat.Data;

        int totalElements = height * width * channels;

        float[] buffer = new float[totalElements];
        Marshal.Copy(matPtr, buffer, 0, totalElements);

        // Convert HWC to NCHW
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width + x) * channels;
                for (int c = 0; c < channels; c++)
                {
                    outTensor[0, c, y, x] = buffer[pixelIndex + c];
                }
            }
        }
    }
}
