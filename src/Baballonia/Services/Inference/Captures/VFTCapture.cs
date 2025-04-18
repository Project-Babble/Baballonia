using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace AvaloniaMiaDev.Services.Inference.Captures;

/// <summary>
/// Vive Facial Tracker camera capture
/// </summary>
public class VftCapture : Capture
{
    /// <summary>
    /// The VideoCapture that grabs frames from the VFT
    /// </summary>
    private VideoCapture? _videoCapture;

    /// <summary>
    /// Gets a raw frame from the camera with timeout for safety.
    /// </summary>
    public override Mat RawMat => _mat;

    private Mat _mat = new();
    private readonly Mat _orignalMat = new();

    public override uint FrameCount { get; protected set; }

    /// <summary>
    /// Retrieves the dimensions of the video frame with timeout.
    /// </summary>
    public override (int width, int height) Dimensions => _dimensions;

    private (int width, int height) _dimensions;

    /// <summary>
    /// Indicates if the camera is ready for capturing frames.
    /// </summary>
    public override bool IsReady { get; protected set; }

    /// <summary>
    /// Camera URL or source identifier.
    /// </summary>
    public override string Url { get; set; } = null!;

    /// <summary>
    /// Constructor that accepts a URL for the video source.
    /// </summary>
    /// <param name="url">URL for video source.</param>
    public VftCapture(string url) : base(url) { }

    private bool _loop;

    /// <summary>
    /// Starts video capture and applies custom resolution and framerate settings.
    /// </summary>
    /// <returns>True if the video capture started successfully, otherwise false.</returns>
    public override async Task<bool> StartCapture()
    {
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            try
            {
                // Open the VFT device and initialize it.
                SetTrackerState(setActive: true);

                // Initialize VideoCapture with URL, timeout for robustness
                // Set capture mode to YUYV
                // Prevent automatic conversion to RGB
                _videoCapture = await Task.Run(() => new VideoCapture(Url, VideoCaptureAPIs.V4L2), cts.Token);
                _videoCapture.Set(VideoCaptureProperties.Mode, 3);
                _videoCapture.Set(VideoCaptureProperties.ConvertRgb, 0);

                _loop = true;
                _ = Task.Run(VideoCapture_UpdateLoop);
            }
            catch (Exception)
            {
                IsReady = false;
                return IsReady;
            }
        }

        IsReady = _videoCapture!.IsOpened();
        return IsReady;
    }

    private Task VideoCapture_UpdateLoop()
    {
        while (_loop)
        {
            try
            {
                // Grab VideoCapture frame and provide proper cropping/transformations/etc
                // Also map YUV color space to RGB
                IsReady = _videoCapture?.Read(_orignalMat) == true;
                Mat yuvConvert = Mat.FromPixelData(400, 400, MatType.CV_8UC2, _orignalMat.Data);
                yuvConvert = yuvConvert.CvtColor(ColorConversionCodes.YUV2GRAY_Y422, 0);
                yuvConvert = yuvConvert.ColRange(new OpenCvSharp.Range(0, 200));
                yuvConvert = yuvConvert.Resize(new Size(400, 400));
                _mat = yuvConvert;
                if (!IsReady) continue;
                FrameCount++;
                _dimensions.width = _mat.Width;
                _dimensions.height = _mat.Height;
            }
            catch (Exception)
            {
                // ignored
            }
        }

        return Task.CompletedTask;
    }

    private void SetTrackerState(bool setActive)
    {
        // Prev: var fd = ViveFacialTracker.open(Url, ViveFacialTracker.FileOpenFlags.O_RDWR);
        var vftFileStream = File.Open(Url, FileMode.Open, FileAccess.ReadWrite);
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
        if (_videoCapture is null)
            return Task.FromResult(false);

        _loop = false;
        IsReady = false;
        _videoCapture.Release();
        _videoCapture.Dispose();
        _videoCapture = null;
        SetTrackerState(false);
        return Task.FromResult(true);
    }
}
