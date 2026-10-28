using Baballonia.CaptureBin.IO;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.Services.events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Desktop.Calibration;

/// <summary>
/// Periodically records a short clip from the face camera and uploads it as a capture .bin,
/// but only while the user has opted into data sharing.
/// Each frame stores the raw camera image in LeftJpeg and the transformed (cropped) image in RightJpeg.
/// </summary>
public class FaceDataCollectionService(
    ILogger<FaceDataCollectionService> logger,
    ILocalSettingsService localSettingsService,
    IFacePipelineEventBus facePipelineEventBus,
    DataUploaderService dataUploaderService)
    : BackgroundService
{
    // Shared with the eye calibration upload; one opt-in covers both
    private const string ShareDataSetting = "AppSettings_ShareEyeData";

    private static readonly TimeSpan CaptureInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(5);
    private const int JpegQuality = 85;
    private const string BinName = "face_capture.bin";

    private readonly object _lock = new();
    private readonly List<Frame> _frames = new();
    private byte[]? _pendingRawJpeg;
    private ulong _pendingRawTimestamp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CaptureInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!localSettingsService.ReadSetting<bool>(ShareDataSetting))
                continue;

            try
            {
                await CaptureAndUploadAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to collect or upload face data");
            }
        }
    }

    private async Task CaptureAndUploadAsync(CancellationToken token)
    {
        List<Frame> frames;
        facePipelineEventBus.Subscribe<FacePipelineEvents.NewFrameEvent>(OnRawFrame);
        facePipelineEventBus.Subscribe<FacePipelineEvents.NewTransformedFrameEvent>(OnTransformedFrame);
        try
        {
            await Task.Delay(CaptureDuration, token);
        }
        finally
        {
            facePipelineEventBus.Unsubscribe<FacePipelineEvents.NewFrameEvent>(OnRawFrame);
            facePipelineEventBus.Unsubscribe<FacePipelineEvents.NewTransformedFrameEvent>(OnTransformedFrame);

            lock (_lock)
            {
                frames = new List<Frame>(_frames);
                _frames.Clear();
                _pendingRawJpeg = null;
            }
        }

        // Face camera isn't running, or the user opted out mid-capture
        if (frames.Count == 0 || !localSettingsService.ReadSetting<bool>(ShareDataSetting))
            return;

        var binPath = Path.Combine(Utils.ModelDataDirectory, BinName);
        CaptureBin.IO.CaptureBin.WriteAll(binPath, frames);
        try
        {
            await dataUploaderService.UploadDataAsync(binPath);
            logger.LogDebug("Uploaded {Count} face frames", frames.Count);
        }
        finally
        {
            File.Delete(binPath);
        }
    }

    // The pipeline publishes the raw frame and then its transformed version synchronously,
    // and disposes the transformed Mat right after, so both are encoded inside the callbacks.
    private void OnRawFrame(FacePipelineEvents.NewFrameEvent e)
    {
        if (e.image == null || e.image.Empty())
            return;

        Cv2.ImEncode(".jpg", e.image, out var rawBuf, [(int)ImwriteFlags.JpegQuality, JpegQuality]);
        lock (_lock)
        {
            _pendingRawJpeg = rawBuf;
            _pendingRawTimestamp = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }
    }

    private void OnTransformedFrame(FacePipelineEvents.NewTransformedFrameEvent e)
    {
        if (e.image == null || e.image.Empty())
            return;

        Cv2.ImEncode(".jpg", e.image, out var transformedBuf, [(int)ImwriteFlags.JpegQuality, JpegQuality]);
        var time = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();

        lock (_lock)
        {
            if (_pendingRawJpeg == null)
                return;

            _frames.Add(new Frame
            {
                Header = new CaptureFrameHeader
                {
                    Timestamp = time,
                    TimestampLeft = _pendingRawTimestamp,
                    TimestampRight = time,
                    JpegDataLeftLength = (uint)_pendingRawJpeg.Length,
                    JpegDataRightLength = (uint)transformedBuf.Length
                },
                LeftJpeg = _pendingRawJpeg,
                RightJpeg = transformedBuf
            });
            _pendingRawJpeg = null;
        }
    }
}
