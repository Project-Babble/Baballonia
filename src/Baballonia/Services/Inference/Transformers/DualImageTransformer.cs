using OpenCvSharp;

namespace Baballonia.Services.Inference;

public class DualImageTransformer : IImageTransformer
{
    public ImageTransformer LeftTransformer = new();
    public ImageTransformer RightTransformer = new();

    /// <summary>
    /// For single-camera "split eye" feeds (one sensor driving both eyes), swap which
    /// half of the frame feeds which eye, so the right half drives the left eye and
    /// vice versa. Left untouched for true dual-camera setups.
    /// </summary>
    public bool SwapEyes { get; set; }


    public Mat? Apply(Mat image)
    {
        // Assuming the frame is wide enough to be split in half
        var width = image.Width;
        var height = image.Height;

        // Split the frame into left and right halves
        var leftHalf = new Rect(0, 0, width / 2, height);
        var rightHalf = new Rect(width / 2, 0, width / 2, height);

        // Single-feed split eye: swap which half feeds which eye.
        if (SwapEyes)
            (leftHalf, rightHalf) = (rightHalf, leftHalf);

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

        // `combined` owns its own data after Merge; free the two transformed halves.
        leftTransformed.Dispose();
        rightTransformed.Dispose();

        return combined;
    }

}
