using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Runtime.InteropServices;

namespace Baballonia.Services.Inference;

public class MatToFloatTensorConverter : IImageConverter
{
    // Reused scratch buffer for the HWC pixel copy. This converter instance is owned by a single
    // pipeline and only ever called from that pipeline's worker thread, so reuse is race-free and
    // avoids allocating ~0.5 MB per frame.
    private float[]? _buffer;

    public void Convert(Mat input, DenseTensor<float> outTensor)
    {
        // Track whether we own `resultMat` so we only dispose Mats we allocated (never `input`).
        bool ownsResult = false;
        Mat resultMat;
        if (input.Type() != MatType.CV_32FC(input.Channels()))
        {
            resultMat = new Mat();
            input.ConvertTo(resultMat, MatType.CV_32FC(input.Channels()), 1f / 255f);
            ownsResult = true;
        }
        else
        {
            resultMat = input;
        }

        try
        {
            Cv2.Resize(resultMat, resultMat, new Size(outTensor.Dimensions[2], outTensor.Dimensions[3]));
            if (!resultMat.IsContinuous())
            {
                var continuous = resultMat.Clone(); // Make it continuous
                if (ownsResult) resultMat.Dispose();
                resultMat = continuous;
                ownsResult = true;
            }

            int height = resultMat.Rows;
            int width = resultMat.Cols;
            int channels = resultMat.Channels();

            int totalElements = height * width * channels;

            if (_buffer == null || _buffer.Length != totalElements)
                _buffer = new float[totalElements];
            var buffer = _buffer;

            Marshal.Copy(resultMat.Data, buffer, 0, totalElements);

            // Convert interleaved HWC -> planar NCHW by writing directly into the tensor's contiguous
            // row-major backing store. The DenseTensor is [1, C, H, W] (see DefaultInferenceRunner),
            // so element [0, c, y, x] lives at linear index c*H*W + y*W + x. This is bit-identical to
            // the old `outTensor[0, c, y, x] = ...` indexer but skips ~H*W*C bounds-checked,
            // multi-dimension index computations per frame.
            int planeStride = outTensor.Dimensions[2] * outTensor.Dimensions[3]; // H*W
            int rowStride = outTensor.Dimensions[3];                             // W
            var span = outTensor.Buffer.Span;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcBase = (y * width + x) * channels;
                    int dstIdx = y * rowStride + x;
                    for (int c = 0; c < channels; c++)
                    {
                        span[dstIdx] = buffer[srcBase + c];
                        dstIdx += planeStride;
                    }
                }
            }
        }
        finally
        {
            if (ownsResult)
                resultMat.Dispose();
        }
    }
}
