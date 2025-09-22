using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Platforms;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baballonia.Services.Inference.VideoSources;

public class SingleCameraSource : IVideoSource
{
    private ILogger _logger;
    public Size CameraSize;
    public string PreferredCapture;
    private string _cameraAddress;
    private PlatformConnector _platformConnector;

    public SingleCameraSource(
        ILogger logger,
        PlatformConnector platformConnector,
        string cameraAddress,
        string preferredCapture = "")
    {
        _logger = logger;
        _platformConnector = platformConnector;
        _cameraAddress = cameraAddress;
        CameraSize = new Size(0, 0);
        PreferredCapture = preferredCapture;
    }

    public bool Start()
    {
        return _platformConnector.Initialize(_cameraAddress, PreferredCapture);
    }

    public bool Stop()
    {
        _platformConnector.Terminate();
        return true;
    }

    /// <summary>
    /// Captures Image and transforms it to target colorspace
    /// </summary>
    /// <param name="color">colorspace to which captured image would be transformed, uses captured image colorspace by default.</param>
    /// <returns>captured image</returns>
    public Mat? GetFrame(ColorType? color = null)
    {
        if (_platformConnector?.Capture == null)
            return null;

        if (!_platformConnector.Capture.IsReady) return null;


        var rawMat = _platformConnector.Capture.AcquireRawMat();
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
