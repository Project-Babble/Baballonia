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
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            try
            {
                if (int.TryParse(Source, out var index))
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

    private Task VideoCapture_UpdateLoop(VideoCapture capture, CancellationToken ct)
    {
        var frame = new Mat();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                IsReady = capture.Read(frame);
                // no need to push empty Mat
                if (IsReady)
                    SetRawMat(frame);
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
