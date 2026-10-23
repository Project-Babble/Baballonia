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
        // A bare "/dev/videoN" path or numeric index is a local device: open it by index, not via the
        // string ctor. The mini runtime has no V4L2 backend, so CAP_ANY falls back to GStreamer's file
        // source and tries to read the char device as a media file ("unable to start pipeline"). Going
        // through FromCamera makes GStreamer build a proper v4l2src pipeline instead.
        var isLocalDevice = int.TryParse(Source, out var index) || TryGetV4l2Index(Source, out index);
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            try
            {
                if (isLocalDevice)
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

        // Fail-fast only for local /dev/video* or index cameras: this build's OpenCV ships only the
        // GStreamer backend, which needs the (unbundled) v4l2src plugin to read them, so a false
        // IsOpened() there is terminal — bail with a pointer to the dependency-free "V4L2 Camera"
        // backend instead of leaving the caller to wait out its frame-arrival timeout.
        //
        // Network/URL sources (http MJPEG streams, appsink pipelines) are different: VideoCapture
        // .IsOpened() can read false right after construction yet still deliver frames once the read
        // loop pumps the stream — which is how IP/streaming cameras opened before this fail-fast was
        // added. For those, start the loop and let the caller's frame-arrival timeout be the real gate.
        if (!IsReady && isLocalDevice)
        {
            if (OperatingSystem.IsLinux())
                logger.LogError(
                    "Could not open '{Source}' via OpenCV's GStreamer backend. Install the v4l2src GStreamer " +
                    "plugin (gst-plugins-good), or use the 'V4L2 Camera' backend which needs no GStreamer.", Source);
            else
                logger.LogError("Could not open '{Source}' via OpenCV.", Source);

            _videoCapture.Dispose();
            _videoCapture = null;
            return false;
        }

        CancellationToken token = _updateTaskCts.Token;
        _updateTask = Task.Run(() => VideoCapture_UpdateLoop(_videoCapture, token));

        return true;
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
                {
                    SetRawMat(frame);
                }
                else
                {
                    frame.Dispose();
                    // A failing read (camera unplugged, or a second handle contending the same
                    // physical device) returns immediately; without this the loop pegs a whole core.
                    // Back off briefly, staying responsive to cancellation.
                    ct.WaitHandle.WaitOne(10);
                }
            }
            catch (Exception)
            {
                ct.WaitHandle.WaitOne(10);
            }
        }

        return Task.CompletedTask;
    }

    public override Task<bool> StopCapture()
    {
        var capture = _videoCapture;
        if (capture is null)
            return Task.FromResult(false);


        IsReady = false;
        _videoCapture = null;
        var updateTask = _updateTask;
        _updateTask = null;
        _updateTaskCts.Cancel();

        Task.Run(() =>
        {
            try { updateTask?.Wait(); } catch { /* loop faulted; release the device anyway */ }
            try { capture.Release(); } catch { /* best-effort */ }
            try { capture.Dispose(); } catch { /* best-effort */ }
        });

        return Task.FromResult(true);
    }
}
