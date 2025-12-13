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

    private async Task VideoCapture_UpdateLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _device != null)
        {
            try
            {
                if (_device.CaptureFrame(out byte[]? frame))
                {
                    Mat mat = Cv2.ImDecode(frame, ImreadModes.Grayscale);
                    SetRawMat(mat);
                }
                else
                {
                    await Task.Delay(1, ct);
                }
            }
            catch(Exception e)
            {
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
