using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.OpenCVCapture;

/// <summary>
/// Wrapper class for OpenCV
/// </summary>
public sealed class OpenCvCapture(string source, ILogger<OpenCvCapture> logger) : Capture(source, logger)
{
    private VideoCapture? _videoCapture;
    private static readonly VideoCaptureAPIs PreferredBackend;

    private Task? _updateTask;
    private readonly CancellationTokenSource _updateTaskCts = new();

    static OpenCvCapture()
    {
        // Choose the most appropriate backend based on the detected OS
        // This is needed to handle concurrent camera access
        if (OperatingSystem.IsWindows())
        {
            PreferredBackend = VideoCaptureAPIs.DSHOW;
        }
        else if (OperatingSystem.IsLinux())
        {
            PreferredBackend = VideoCaptureAPIs.GSTREAMER;
        }
        else if (OperatingSystem.IsMacOS())
        {
            PreferredBackend = VideoCaptureAPIs.AVFOUNDATION;
        }
        else
        {
            // Fallback to ANY which lets OpenCV choose
            PreferredBackend = VideoCaptureAPIs.ANY;
        }
    }

    public override async Task<bool> StartCapture()
    {
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            try
            {
                // A bare "/dev/videoN" path must open by index, not via the string ctor: the mini
                // runtime has no V4L2 backend, so CAP_ANY falls back to GStreamer's file source and
                // tries to read the char device as a media file ("unable to start pipeline"). Going
                // through FromCamera makes GStreamer build a proper v4l2src pipeline instead.
                if (int.TryParse(Source, out var index) || TryGetV4l2Index(Source, out index))
                    _videoCapture = await Task.Run(() => VideoCapture.FromCamera(index, PreferredBackend), cts.Token);
                else
                    _videoCapture = await Task.Run(() => new VideoCapture(Source), cts.Token);
            }
            catch (Exception e)
            {
                logger.LogError("Error: {}", e);
                IsReady = false;
                return false;
            }
        }

        // Handle edge case cameras like the Varjo Aero that send frames in YUV
        // This won't activate the IR illuminators, but it's a good idea to standardize inputs
        _videoCapture.ConvertRgb = true;
        IsReady = _videoCapture.IsOpened();

        CancellationToken token = _updateTaskCts.Token;
        _updateTask = Task.Run(() => VideoCapture_UpdateLoop(_videoCapture, token));

        return IsReady;
    }

    // Parses the index out of a Linux "/dev/videoN" path so it can be opened via FromCamera.
    private static bool TryGetV4l2Index(string source, out int index)
    {
        index = 0;
        const string prefix = "/dev/video";
        return source.StartsWith(prefix) && int.TryParse(source.AsSpan(prefix.Length), out index);
    }

    private Task VideoCapture_UpdateLoop(VideoCapture capture, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Fresh Mat per frame: SetRawMat hands ownership to the consumer, so reusing
                // one buffer races the next Read against it and stalls the feed.
                var frame = new Mat();
                IsReady = capture.Read(frame);
                if (IsReady)
                    SetRawMat(frame);
                else
                    frame.Dispose();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        return Task.CompletedTask;
    }

    public override Task<bool> StopCapture()
    {
        if (_videoCapture is null)
            return Task.FromResult(false);

        if (_updateTask != null) {
            _updateTaskCts.Cancel();
            _updateTask.Wait();
        }

        IsReady = false;
        if (_videoCapture != null)
        {
            _videoCapture.Release();
            _videoCapture.Dispose();
            _videoCapture = null;
        }
        return Task.FromResult(true);
    }
}
