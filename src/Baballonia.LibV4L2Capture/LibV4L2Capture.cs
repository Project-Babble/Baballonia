using System.Runtime.InteropServices;
using Baballonia.LibV4L2Capture.V4L2;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.LibV4L2Capture;

public sealed class LibV4L2Capture(string source, ILogger<LibV4L2Capture> logger) : Capture(source, logger)
{
    private Device? _device;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    public override Task<bool> StartCapture()
    {
        try
        {
            _device = Device.Connect(Source);

            if (_device == null)
                return Task.FromResult(false);

            Logger.LogInformation($"Using pixel format: {_device.PixelFormat}");

            _device.StartCapture();
            IsReady = true;
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return Task.FromResult(false);
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _captureTask = Task.Run(() => VideoCapture_UpdateLoop(token), token);

        return Task.FromResult(true);
    }

    private void DecodeMJPEG(byte[] frame)
    {
        var mat = Cv2.ImDecode(frame, ImreadModes.Grayscale);
        SetRawMat(mat);
    }

    private void DecodeYUYV(byte[] frame, uint width, uint height)
    {
        var yuyvMat = new Mat((int)height, (int)width, MatType.CV_8UC2);
        Marshal.Copy(frame, 0, yuyvMat.Data, frame.Length);

        var grayMat = new Mat();
        Cv2.CvtColor(yuyvMat, grayMat, ColorConversionCodes.YUV2GRAY_YUY2);
        SetRawMat(grayMat);
    }

    private async Task VideoCapture_UpdateLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _device != null)
        {
            try
            {
                if (_device.CaptureFrame(out byte[]? frame))
                {
                    if (frame is { Length: > 0 })
                    {
                        switch (_device.PixelFormat)
                        {
                            case v4l2_pix_fmt.V4L2_PIX_FMT_MJPEG:
                                DecodeMJPEG(frame);
                                break;
                            case v4l2_pix_fmt.V4L2_PIX_FMT_YUYV:
                                var pix = _device.CurrentFormat.pix;
                                DecodeYUYV(frame, pix.width, pix.height);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
                else
                {
                    await Task.Delay(1, ct);
                }
            }
            // catch (TaskCanceledException)
            // {
            //     return;
            // }
            catch(Exception e)
            {
                SetRawMat(new Mat());
                IsReady = false;
                Logger.LogError(e.ToString());
                _device.Dispose();
                break;
            }
        }
    }

    public override Task<bool> StopCapture()
    {
        if (_device is null)
            return Task.FromResult(false);

        if (_captureTask != null)
        {
            _cts?.Cancel();
            _captureTask.Wait();
        }

        IsReady = false;
        _device?.Dispose();
        _device = null;
        return Task.FromResult(true);
    }
}
