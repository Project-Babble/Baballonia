using System;
using Baballonia.Services.Inference.Enums;
using OpenCvSharp;

namespace Baballonia.Services.Inference;

public class DualCameraSource : IVideoSource
{
    public IVideoSource? LeftCam;
    public IVideoSource? RightCam;

    private Mat? LastLeftImage;
    private Mat? LastRightImage;

    public bool Start()
    {
        return (LeftCam?.Stop() ?? true) && (RightCam?.Stop() ?? true);
    }

    public bool Stop()
    {
        return (LeftCam?.Start() ?? true) && (RightCam?.Start() ?? true);
    }

    // Here we try to acquire 2 images from both cameras and stitch them into a single image
    // if at least one image can be acquired, try to stitch it with last second image
    public Mat? GetFrame(ColorType? color = null)
    {
        var leftImage = LeftCam?.GetFrame(color);
        var rightImage = RightCam?.GetFrame(color);

        if (leftImage == null && rightImage == null)
            return null;

        // Track which images are new vs fallback
        var leftIsNew = leftImage != null;
        var rightIsNew = rightImage != null;

        leftImage ??= LastLeftImage;
        rightImage ??= LastRightImage;

        switch (leftImage)
        {
            case null when rightImage == null:
                return null;
            case null:
                leftImage = rightImage.Clone();
                break;
            default:
                // Don't clone - preserve both cameras when both are available!
                if (rightImage == null)
                    rightImage = leftImage.Clone();
                break;
        }

        int minHeight = Math.Min(leftImage.Rows, rightImage.Rows);
        int minWidth = Math.Min(leftImage.Cols, rightImage.Cols);


        Mat resizedLeft = new Mat();
        Mat resizedRight = new Mat();
        Cv2.Resize(leftImage, resizedLeft, new Size(minWidth, minHeight));
        Cv2.Resize(rightImage, resizedRight, new Size(minWidth, minHeight));

        int height = Math.Max(resizedRight.Rows, resizedLeft.Rows);
        int width = resizedRight.Cols + resizedLeft.Cols;

        Mat result = new Mat(height, width, resizedLeft.Type(), Scalar.All(0));

        resizedLeft.CopyTo(result[new Rect(0, 0, resizedLeft.Cols, resizedRight.Rows)]);
        resizedRight.CopyTo(result[new Rect(resizedLeft.Cols, 0, resizedRight.Cols, resizedRight.Rows)]);

        // Update last images only for new frames
        if (leftIsNew)
        {
            LastLeftImage?.Dispose();
            LastLeftImage = resizedLeft.Clone();
        }

        if (rightIsNew)
        {
            LastRightImage?.Dispose();
            LastRightImage = resizedRight.Clone();
        }

        // Only dispose new images, not the cached LastImages
        if (leftIsNew)
            resizedLeft.Dispose();
        if (rightIsNew)
            resizedRight.Dispose();

        return result;
    }

    public void Dispose()
    {
        LeftCam?.Dispose();
        RightCam?.Dispose();
        LastLeftImage?.Dispose();
        LastRightImage?.Dispose();
    }
}
