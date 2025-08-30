using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.OpenCVCapture;

/// <summary>
/// Wrapper class for OpenCV
/// </summary>
public sealed class OpenCvCapture(string source, ILogger logger) : Capture(source, logger)
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

    public override bool CanConnect(string connectionString)
    {
        var lowered = connectionString.ToLower();
        var serial = lowered.StartsWith("com") ||
                     lowered.StartsWith("/dev/tty") ||
                     lowered.StartsWith("/dev/cu") ||
                     lowered.StartsWith("/dev/ttyacm");;
        if (serial) return false;

        return lowered.StartsWith("/dev/video") ||
               lowered.EndsWith("appsink") ||
               int.TryParse(connectionString, out _) ||
               Uri.TryCreate(connectionString, UriKind.Absolute, out _);
    }

    public override async Task<bool> StartCapture()
    {
        Logger.LogDebug("Starting OpenCV capture...");
        Logger.LogDebug("Camera Source URL: '" + Source + "'");
        Logger.LogDebug("Preferred Backend: " + PreferredBackend);
        Logger.LogDebug("OpenCV Version: " + Cv2.GetVersionString());

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            try
            {
                if (int.TryParse(Source, out var index))
                {
                    Logger.LogDebug("Source is numeric camera index: " + index);
                    Logger.LogDebug("Creating VideoCapture from camera index " + index + " with backend " + PreferredBackend);
                    _videoCapture = await Task.Run(() => VideoCapture.FromCamera(index, PreferredBackend), cts.Token);
                }
                else
                {
                    Logger.LogDebug("Source is string-based: '" + Source + "'");
                    Logger.LogDebug("Creating VideoCapture from URL string (backend will be auto-detected)");
                    _videoCapture = await Task.Run(() => new VideoCapture(Source), cts.Token);
                }

                Logger.LogDebug("VideoCapture instance created successfully");
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("Camera capture initialization timed out after 5 seconds for Source: '" + Source + "'");
                IsReady = false;
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error creating VideoCapture for Source: '" + Source + "'");
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

        Logger.LogDebug("Started OpenCV capture: {IsReady}", IsReady);
        return IsReady;
    }

    private Task VideoCapture_UpdateLoop(VideoCapture capture, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                IsReady = capture.Read(RawMat);
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
        Logger.LogDebug("StopCapture requested for camera Source '{Source}'", Source);
        if (_videoCapture is null)
        {
            Logger.LogDebug("StopCapture: VideoCapture is already null, returning false");
            return Task.FromResult(false);
        }

        if (_updateTask != null) {
            _updateTaskCts.Cancel();
            _updateTask.Wait();
            Logger.LogDebug("Video capture update task cancelled successfully");
        }

        Logger.LogDebug("Cancelling video capture update task...");
        IsReady = false;
        if (_videoCapture != null)
        {
            Logger.LogDebug("Releasing and disposing VideoCapture instance...");
            _videoCapture.Release();
            _videoCapture.Dispose();
            _videoCapture = null;
            Logger.LogDebug("VideoCapture released and disposed successfully");
        }

        Logger.LogDebug("Camera capture stopped successfully for Source '{Source}'", Source);
        return Task.FromResult(true);
    }
}
