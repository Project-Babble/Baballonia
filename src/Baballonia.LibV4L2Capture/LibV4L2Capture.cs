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
    private Thread? _captureThread;

    // How long poll() blocks waiting for a frame before the loop re-checks cancellation. Bounds
    // worst-case shutdown latency; never hit during normal streaming (a frame arrives every ~8 ms).
    private const int FramePollTimeoutMs = 200;

    public override double TargetFps => _device?.Fps ?? 0;
    public override string PixelFormatName => _device?.PixelFormat.ToString() ?? "";

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

        // Dedicated thread: the loop blocks in-kernel on poll() waiting for frames, which would be
        // inappropriate on a pooled thread.
        _captureThread = new Thread(() => VideoCapture_UpdateLoop(token))
        {
            IsBackground = true,
            Name = "V4L2Capture"
        };
        _captureThread.Start();

        return Task.FromResult(true);
    }

    private void DecodeMJPEG(byte[] frame)
    {
        var mat = Cv2.ImDecode(frame, ImreadModes.Grayscale);
        SetRawMat(mat);
    }

    private void DecodeYUYV(byte[] frame, uint width, uint height)
    {
        using var yuyvMat = new Mat((int)height, (int)width, MatType.CV_8UC2);
        Marshal.Copy(frame, 0, yuyvMat.Data, frame.Length);

        var grayMat = new Mat();
        Cv2.CvtColor(yuyvMat, grayMat, ColorConversionCodes.YUV2GRAY_YUY2);
        SetRawMat(grayMat);
    }

    private void VideoCapture_UpdateLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _device != null)
        {
            try
            {
                // Blocks in-kernel until a frame is ready or FramePollTimeoutMs elapses, so the
                // thread sleeps between frames instead of busy-polling + Task.Delay(1).
                if (_device.CaptureFrame(out byte[]? frame, FramePollTimeoutMs))
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
            }
            catch(Exception e)
            {
                SetRawMat(new Mat());
                IsReady = false;
                Logger.LogError(e.ToString());
                _device?.Dispose();
                break;
            }
        }
    }

    public override Task<bool> StopCapture()
    {
        if (_device is null)
            return Task.FromResult(false);

        // Signal the loop to stop; it wakes within FramePollTimeoutMs (poll() doesn't observe the
        // token). Join is bounded so a wedged device can never hang shutdown indefinitely.
        _cts?.Cancel();
        if (_captureThread is { IsAlive: true })
            _captureThread.Join(TimeSpan.FromSeconds(2));

        IsReady = false;
        _device?.Dispose();
        _device = null;
        return Task.FromResult(true);
    }
}
