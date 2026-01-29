using System.Runtime.InteropServices;
using Baballonia.VFTCapture.Linux.V4L2;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.VFTCapture.Linux;

/// <summary>
/// Vive Facial Tracker camera capture
/// </summary>
public sealed class LinuxVftCapture(string source, ILogger logger) : Capture(source, logger)
{
    private Device? _device;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private bool _loop;

    /// <summary>
    /// Starts video capture and applies custom resolution and framerate settings.
    /// </summary>
    /// <returns>True if the video capture started successfully, otherwise false.</returns>
    public override async Task<bool> StartCapture()
    {
        Logger.LogDebug("Starting VFT camera capture...");

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            try
            {
                SetTrackerState(setActive: true);

                _device = Device.Connect(Source);

                if (_device == null)
                {
                    Logger.LogError("Failed to connect to VFT device via V4L2");
                    IsReady = false;
                    return IsReady;
                }

                // Set capture mode to YUYV if possible
                // Logger.LogInformation($"Using pixel format: {_device.PixelFormat}");
                // if (_device.PixelFormat != v4l2_pix_fmt.V4L2_PIX_FMT_YUYV)
                // {
                //     Logger.LogWarning($"Device pixel format is {_device.PixelFormat}, expected YUYV");
                // }

                _device.StartCapture();
                _loop = true;

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _captureTask = Task.Run(() => VideoCapture_UpdateLoop(token), token);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to start VFT camera capture");
                IsReady = false;
                return IsReady;
            }
        }

        IsReady = _device != null;
        Logger.LogDebug("VFT camera capture started successfully: " + IsReady);
        return IsReady;
    }

    private async Task VideoCapture_UpdateLoop(CancellationToken ct)
    {
        Mat yuvConvert = new();
        Mat yuyvMat = new();
        while (!ct.IsCancellationRequested && _loop && _device != null)
        {
            try
            {
                if (_device.CaptureFrame(out byte[]? frame))
                {
                    if (frame is { Length: > 0 })
                    {
                        IsReady = true;

                        // Convert YUYV frame to Mat for processing
                        var pix = _device.CurrentFormat.pix;
                        yuyvMat = new Mat((int)pix.height, (int)pix.width, MatType.CV_8UC2);
                        Marshal.Copy(frame, 0, yuyvMat.Data, frame.Length);

                        // Apply the same processing pipeline as before
                        yuvConvert = yuyvMat.CvtColor(ColorConversionCodes.YUV2GRAY_Y422, 0);
                        yuvConvert = yuvConvert.ColRange(VFTCommon.ColumnRange);
                        yuvConvert = yuvConvert.Resize(VFTCommon.ImageSize);
                        yuvConvert = yuvConvert.GaussianBlur(VFTCommon.GaussianBlurSize, 0);

                        var rawMat = yuvConvert.LUT(VFTCommon.Lut);
                        SetRawMat(rawMat);
                    }
                    else
                    {
                        IsReady = false;
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
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in VFT capture loop");
                IsReady = false;
                await Task.Delay(10, ct);
            }
        }

        yuvConvert.Dispose();
        yuyvMat.Dispose();
    }

    private void SetTrackerState(bool setActive)
    {
        try
        {
            // Leverage IDisposable for GC-less release of handle.
            using var device = new LinuxUsbCommunicator(Logger, Source);
            if (!device.IsValid)
                throw new NullReferenceException();

            device.SetState(setActive);
        }
        catch(Exception e)
        {
            Logger.LogError(e.Message);
        }
    }

    /// <summary>
    /// Stops video capture and cleans up resources.
    /// </summary>
    /// <returns>True if capture stopped successfully, otherwise false.</returns>
    public override Task<bool> StopCapture()
    {
        Logger.LogDebug("Stopping VFT camera capture...");

        if (_device is null)
        {
            Logger.LogDebug("VFT Device is already null, returning false");
            return Task.FromResult(false);
        }

        _loop = false;
        IsReady = false;

        if (_captureTask != null)
        {
            _cts?.Cancel();
            _captureTask.Wait();
        }

        _device.Dispose();
        _device = null;
        SetTrackerState(false);
        Logger.LogDebug("VFT camera capture stopped successfully");
        return Task.FromResult(true);
    }
}
