using System;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using OpenCvSharp;

namespace Baballonia.Services.Inference;

public class ImageTransformer : IImageTransformer
{
    public CameraSettings Transformation;
    public Size TargetSize = new Size(224,224);

    public ImageTransformer()
    {
        var roi = new RegionOfInterest();
        Transformation = new CameraSettings(Camera.Face, roi);
    }

    public Mat? Apply(Mat image)
    {
        int roiX = (int)Transformation.Roi.X;
        int roiY = (int)Transformation.Roi.Y;
        int roiWidth = (int)Transformation.Roi.Width;
        int roiHeight = (int)Transformation.Roi.Height;
        int maxWidth = image.Width;
        int maxHeight = image.Height;

        // Ensure ROI is within bounds
        Rect roi;
        if (roiX < 0 || roiY < 0 || roiWidth <= 0 || roiHeight <= 0 ||
            roiX + roiWidth > maxWidth || roiY + roiHeight > maxHeight ||
            roiWidth == maxWidth || roiHeight == maxHeight)
        {
            roi = new Rect(0, 0, maxWidth, maxHeight);
        }
        else
        {
            roi = new Rect(roiX, roiY, roiWidth, roiHeight);
        }

        using var roiMat = new Mat(image, roi);

        Mat resultMat = roiMat.Clone();

        var channels = resultMat.Channels();
        // Convert to grayscale or extract red channel
        if (channels >= 2)
        {
            var newMat = new Mat();
            if (Transformation.UseRedChannel)
                Cv2.ExtractChannel(resultMat, newMat, 0);
            else
                Cv2.CvtColor(resultMat, newMat, ColorConversionCodes.BGR2GRAY);

            resultMat.Dispose();
            resultMat = newMat;
        }

        // Adjust brightness
        if (Transformation.Gamma is not 1f)
        {
            var newMat = new Mat();
            resultMat.ConvertTo(newMat, image.Type(),Transformation.Gamma);
            resultMat.Dispose();
            resultMat = newMat;
        }

        // Adjust brightness and type conversion

        double rotationRadians = Transformation.RotationRadians;
        bool hFlip = Transformation.UseHorizontalFlip;
        bool vFlip = Transformation.UseVerticalFlip;

        if (rotationRadians != 0 || hFlip || vFlip)
        {
            double cos = Math.Cos(rotationRadians), sin = Math.Sin(rotationRadians);
            double scale = 1.0 / (Math.Abs(cos) + Math.Abs(sin));
            double hscale = (hFlip ? -1.0 : 1.0) * scale;
            double vscale = (vFlip ? -1.0 : 1.0) * scale;

            using var matrix = new Mat<double>(2, 3);
            Span<double> data = matrix.AsSpan<double>();

            data[0] = (double)TargetSize.Width / resultMat.Width * cos * hscale;
            data[1] = (double)TargetSize.Height / resultMat.Height * sin * hscale;
            data[2] = ((double)TargetSize.Width -
                       ((double)TargetSize.Width * cos + (double)TargetSize.Height * sin) * hscale) * 0.5;

            data[3] = -(double)TargetSize.Width / resultMat.Width * sin * vscale;
            data[4] = (double)TargetSize.Height / resultMat.Height * cos * vscale;
            data[5] = ((double)TargetSize.Height +
                       ((double)TargetSize.Width * sin - (double)TargetSize.Height * cos) * vscale) * 0.5;


            Cv2.WarpAffine(resultMat, resultMat, matrix, TargetSize);
        }
        else
        {
            try
            {
                Cv2.Resize(resultMat, resultMat, TargetSize);
            }
            catch
            {
                resultMat.Dispose();
                return null;
            }
        }

        return resultMat;
    }

}
