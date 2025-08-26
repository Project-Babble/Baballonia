using System.Collections.Generic;
using System.Linq;
using Baballonia.Services.Inference.Models;
using OpenCvSharp;

namespace Baballonia.Services.Inference;

public class DualImageTransformer : IImageTransformer
{
    public ImageTransformer LeftTransformer = new();
    public ImageTransformer RightTransformer = new();


    public Mat? Apply(Mat image)
    {
        // Assuming the frame is wide enough to be split in half
        var width = image.Width;
        var height = image.Height;

        // Split the frame into left and right halves
        var leftHalf = new Rect(0, 0, width / 2, height);
        var rightHalf = new Rect(width / 2, 0, width / 2, height);

        // Create ROIs for left and right eyes
        using var leftRoi = new Mat(image, leftHalf);
        using var rightRoi = new Mat(image, rightHalf);

        // transform both simultaneously with same settings
        var leftTransformed = LeftTransformer.Apply(leftRoi);
        var rightTransformed =  RightTransformer.Apply(rightRoi);
        if (leftTransformed == null || rightTransformed == null)
        {
            leftTransformed?.Dispose();
            rightTransformed?.Dispose();
            return null;
        }

        var combined = new Mat();
        Cv2.Merge([leftTransformed, rightTransformed], combined);

        return combined;
    }

}
