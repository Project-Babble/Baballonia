using Baballonia.SDK;
using Baballonia.Services.Inference.Enums;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baballonia.Services.Inference.VideoSources;

public class SingleCameraSource : IVideoSource
{
    private ILogger _logger;
    public Size CameraSize;
    private string _cameraAddress;
    private readonly Capture _capture;

    public SingleCameraSource(
        ILogger logger,
        Capture capture,
        string cameraAddress)
    {
        _logger = logger;
        _capture = capture;
        _cameraAddress = cameraAddress;
        CameraSize = new Size(0, 0);
    }

    public bool Start()
    {
        _capture.StartCapture();
        return true;
    }

    public bool Stop()
    {
        _capture.StopCapture();
        return true;
    }

    /// <summary>
    /// Captures Image and transforms it to target colorspace
    /// </summary>
    /// <param name="color">colorspace to which captured image would be transformed, uses captured image colorspace by default.</param>
    /// <returns>captured image</returns>
    public Mat? GetFrame(ColorType? color = null)
    {
        if (!_capture.IsReady) return null;


        var rawMat = _capture.AcquireRawMat();
        if (rawMat == null)
            return null;

        CameraSize.Width = rawMat.Width;
        CameraSize.Height = rawMat.Height;

        Mat image;
        if (color == null ||
            color == (rawMat.Channels() == 1 ? ColorType.Gray8 : ColorType.Bgr24))
        {
            image = rawMat;
        }
        else
        {
            var convertedMat = new Mat();
            Cv2.CvtColor(rawMat, convertedMat,
                (rawMat.Channels() == 1)
                    ? color switch
                    {
                        ColorType.Bgr24 => ColorConversionCodes.GRAY2BGR,
                        ColorType.Rgb24 => ColorConversionCodes.GRAY2RGB,
                        ColorType.Rgba32 => ColorConversionCodes.GRAY2RGBA,
                    }
                    : color switch
                    {
                        ColorType.Gray8 => ColorConversionCodes.BGR2GRAY,
                        ColorType.Rgb24 => ColorConversionCodes.BGR2RGB,
                        ColorType.Rgba32 => ColorConversionCodes.BGR2RGBA,
                    });
            image = convertedMat;
        }


        return image;
    }

    public void Dispose()
    {
        Stop();
    }
}
