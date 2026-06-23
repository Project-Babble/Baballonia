using System.Diagnostics;
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
        var hasIndex = int.TryParse(Source, out var index);

        // A non-Y800 mode (FourCC + resolution) to force, since the OpenCV DSHOW backend can't
        // stream Y800 - which many trackers expose as their default pin. Enumerated up front so
        // we don't contend with OpenCV for the device.
        (string FourCc, int Width, int Height, double Fps)? forcedMode = null;
#if WINDOWS
        if (OperatingSystem.IsWindows() && hasIndex && PreferredBackend == VideoCaptureAPIs.DSHOW)
        {
            try
            {
                if (DirectShowModeSelector.SelectBestSupportedMode(index, logger) is { } m)
                    forcedMode = (m.FourCc, m.Width, m.Height, m.Fps);
            }
            catch (Exception e)
            {
                logger.LogWarning("DirectShow mode selection failed, using OpenCV defaults: {}", e.Message);
            }
        }
#endif

        var (capture, gotFrame) = await Task.Run(() => TryOpen(PreferredBackend, hasIndex, index, forcedMode));

#if WINDOWS
        // OpenCV's DSHOW backend (videoInput) opens some UVC trackers but never delivers frames,
        // even when pointed at a valid MJPG pin. Media Foundation drives them correctly. Only used
        // when DSHOW produced nothing, so cameras that already work on DSHOW are unaffected.
        if (!gotFrame && OperatingSystem.IsWindows() && hasIndex && PreferredBackend == VideoCaptureAPIs.DSHOW)
        {
            capture?.Release();
            capture?.Dispose();
            logger.LogWarning("DSHOW delivered no frames for '{}'; retrying with Media Foundation (MSMF).", Source);
            (capture, gotFrame) = await Task.Run(() => TryOpen(VideoCaptureAPIs.MSMF, hasIndex, index, forcedMode));
        }
#endif

        if (capture is null)
        {
            IsReady = false;
            return false;
        }

        _videoCapture = capture;
        IsReady = _videoCapture.IsOpened();

        CancellationToken token = _updateTaskCts.Token;
        _updateTask = Task.Run(() => VideoCapture_UpdateLoop(_videoCapture, token));

        return IsReady;
    }

    /// <summary>
    /// Opens the device with a specific backend, applies the forced mode, and verifies frames
    /// actually flow (a short, sleeping probe - no busy-wait). Returns the capture (or null) and
    /// whether a real frame was received, so the caller can fall back to another backend.
    /// </summary>
    private (VideoCapture? capture, bool gotFrame) TryOpen(
        VideoCaptureAPIs backend, bool hasIndex, int index, (string FourCc, int Width, int Height, double Fps)? mode)
    {
        VideoCapture capture;
        try
        {
            capture = hasIndex ? VideoCapture.FromCamera(index, backend) : new VideoCapture(Source);
        }
        catch (Exception e)
        {
            logger.LogWarning("Opening '{}' with {} threw: {}", Source, backend, e.Message);
            return (null, false);
        }

        if (!capture.IsOpened())
        {
            logger.LogWarning("Backend {} could not open '{}'.", backend, Source);
            capture.Dispose();
            return (null, false);
        }

        if (mode is { } m)
        {
            // Order matters: size, then fps, then FourCC last. When a camera exposes the same
            // resolution as both a raw pin and a compressed (MJPG) pin, the *frame rate* is what
            // disambiguates them - e.g. only the MJPG pin offers 120fps. Setting fps drives the
            // backend onto the streamable compressed pin; setting FourCC last pins the codec.
            if (m.Width > 0) capture.Set(VideoCaptureProperties.FrameWidth, m.Width);
            if (m.Height > 0) capture.Set(VideoCaptureProperties.FrameHeight, m.Height);
            if (m.Fps > 0) capture.Set(VideoCaptureProperties.Fps, m.Fps);
            capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC(m.FourCc));
        }

        // Handle edge case cameras like the Varjo Aero that send frames in YUV
        // This won't activate the IR illuminators, but it's a good idea to standardize inputs
        capture.ConvertRgb = true;

        logger.LogInformation("Backend {} opened '{}'; negotiated {}x{}@{}fps fourcc={}.",
            backend, Source,
            (int)capture.Get(VideoCaptureProperties.FrameWidth),
            (int)capture.Get(VideoCaptureProperties.FrameHeight),
            (int)capture.Get(VideoCaptureProperties.Fps),
            DecodeFourCc(capture.Get(VideoCaptureProperties.FourCC)));

        using var probe = new Mat();
        var sw = Stopwatch.StartNew();
        var gotFrame = false;
        while (sw.Elapsed < TimeSpan.FromSeconds(2))
        {
            if (capture.Read(probe) && !probe.Empty())
            {
                gotFrame = true;
                break;
            }
            Thread.Sleep(10);
        }

        logger.LogInformation("Backend {} frame probe for '{}': {} (after {}ms).",
            backend, Source, gotFrame ? "frames flowing" : "NO FRAMES", (int)sw.ElapsedMilliseconds);
        return (capture, gotFrame);
    }

    private static string DecodeFourCc(double value)
    {
        var f = (int)value;
        if (f == 0) return "0";
        var chars = new[] { (char)(f & 0xFF), (char)((f >> 8) & 0xFF), (char)((f >> 16) & 0xFF), (char)((f >> 24) & 0xFF) };
        foreach (var ch in chars)
            if (ch < 0x20 || ch > 0x7E) return f.ToString();
        return new string(chars);
    }

    private Task VideoCapture_UpdateLoop(VideoCapture capture, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // A fresh Mat per delivered frame: SetRawMat transfers ownership to the consumer, so
            // this buffer must not be reused. Reusing it lets the next capture.Read() write into a
            // Mat the consumer is still reading/disposing on another thread -> native crash
            // (access violation), which only shows up once frames actually start flowing.
            var frame = new Mat();
            try
            {
                if (capture.Read(frame) && !frame.Empty())
                {
                    IsReady = true;
                    SetRawMat(frame); // ownership handed off; do not touch `frame` after this
                }
                else
                {
                    IsReady = false;
                    frame.Dispose();
                    if (!ct.IsCancellationRequested)
                        Thread.Sleep(10); // don't peg a core while no frames are arriving
                }
            }
            catch (Exception)
            {
                frame.Dispose();
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
