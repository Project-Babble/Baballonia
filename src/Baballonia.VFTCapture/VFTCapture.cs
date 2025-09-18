
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.VFTCapture;

/// <summary>
/// Vive Facial Tracker camera capture
/// </summary>
public sealed class VftCapture(string source, ILogger logger) : Capture(source, logger)
{
    private VideoCapture? _videoCapture;
    private readonly Mat _originalMat = new();
    private bool _loop;

    public override bool CanConnect(string connectionString)
    {
        var lowered = connectionString.ToLower();
        return lowered.StartsWith("/dev/video") && OperatingSystem.IsLinux();
    }

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
                // Open the VFT device and initialize it.
                SetTrackerState(setActive: true);

                // Initialize VideoCapture with URL, timeout for robustness
                // Set capture mode to YUYV
                // Prevent automatic conversion to RGB
                _videoCapture = await Task.Run(() => new VideoCapture(Source, VideoCaptureAPIs.V4L2), cts.Token);
                _videoCapture.Set(VideoCaptureProperties.Mode, 3);
                _videoCapture.Set(VideoCaptureProperties.ConvertRgb, 0);

                _loop = true;
                _ = Task.Run(VideoCapture_UpdateLoop);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to start VFT camera capture");
                IsReady = false;
                return IsReady;
            }
        }

        IsReady = _videoCapture!.IsOpened();
        Logger.LogDebug("VFT camera capture started successfully: " + IsReady);
        return IsReady;
    }

    private Task VideoCapture_UpdateLoop()
    {
        Mat lut = new Mat(new Size(1,256), MatType.CV_8U);
        for (var i = 0; i <= 255; i++)
        {
            lut.Set(i, (byte)(Math.Pow(i / 2048.0, (1 / 2.5)) * 255.0));
        }
        while (_loop)
        {
            try
            {
                IsReady = _videoCapture?.Read(_originalMat) == true;
                if (IsReady)
                {
                    var yuvConvert = Mat.FromPixelData(400, 400, MatType.CV_8UC2, _originalMat.Data);
                    yuvConvert = yuvConvert.CvtColor(ColorConversionCodes.YUV2GRAY_Y422, 0);
                    yuvConvert = yuvConvert.ColRange(new OpenCvSharp.Range(0, 200));
                    yuvConvert = yuvConvert.Resize(new Size(400, 400));
                    yuvConvert = yuvConvert.GaussianBlur(new Size(15, 15), 0);

                    var rawMat = yuvConvert.LUT(lut);
                    SetRawMat(rawMat);

                    yuvConvert.Dispose();
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
        lut.Dispose();

        return Task.CompletedTask;
    }

    private void SetTrackerState(bool setActive)
    {
        // Prev: var fd = ViveFacialTracker.open(Url, ViveFacialTracker.FileOpenFlags.O_RDWR);
        var vftFileStream = File.Open(Source, FileMode.Open, FileAccess.ReadWrite);
        var fd = vftFileStream.SafeFileHandle.DangerousGetHandle();
        if (fd != IntPtr.Zero)
        {
            try
            {
                // Activate the tracker and give it some time to warm up/cool down
                if (setActive)
                    ViveFacialTracker.activate_tracker((int)fd);
                else
                    ViveFacialTracker.deactivate_tracker((int)fd);
                // await Task.Delay(1000);
            }
            finally
            {
                // Prev: ViveFacialTracker.close((int)fd);
                vftFileStream.Close();
            }
        }
    }

    /// <summary>
    /// Stops video capture and cleans up resources.
    /// </summary>
    /// <returns>True if capture stopped successfully, otherwise false.</returns>
    public override Task<bool> StopCapture()
    {
        Logger.LogDebug("Stopping VFT camera capture...");

        if (_videoCapture is null)
        {
            Logger.LogDebug("VFT VideoCapture is already null, returning false");
            return Task.FromResult(false);
        }

        _loop = false;
        IsReady = false;
        _videoCapture.Release();
        _videoCapture.Dispose();
        _videoCapture = null;
        SetTrackerState(false);
        Logger.LogDebug("VFT camera capture stopped successfully");
        return Task.FromResult(true);
    }
}
