using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Baballonia.CaptureBin.IO;
using OpenCvSharp;
using OverlaySDK.Packets;

namespace Baballonia.Desktop.Calibration;

public class PositionalBinCollector
{
    private readonly object _lock = new();
    private readonly List<Frame> _frames = new();
    private HmdPositionalDataPacket? _latestPosData;
    private uint _headerFlags;

    public PositionalBinCollector(uint headerFlags)
    {
        _headerFlags = headerFlags;
    }

    public void UpdatePositionalData(HmdPositionalDataPacket posData)
    {
        Interlocked.Exchange(ref _latestPosData, posData);
    }

    CaptureFrameHeader GenerateHeader(HmdPositionalDataPacket positionalData)
    {
        var time = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();
        return new CaptureFrameHeader
        {
            RoutineState = _headerFlags,
            LeftEyePitch = -positionalData.LeftEyePitch, // Flip this
            LeftEyeYaw = positionalData.LeftEyeYaw,
            RightEyePitch = -positionalData.RightEyePitch, // Flip this
            RightEyeYaw = positionalData.RightEyeYaw,
            RoutinePitch = positionalData.RoutinePitch,
            RoutineYaw = positionalData.RoutineYaw,
            RoutineDistance = positionalData.RoutineDistance,
            RoutineConvergence = positionalData.RoutineConvergence,
            FovAdjustDistance = positionalData.FovAdjustDistance,
            Timestamp = time,
            TimestampLeft = time,
            TimestampRight = time,
        };
    }

    public Frame? AddFrame(Mat left, Mat right)
    {
        var posData = Interlocked.CompareExchange(ref _latestPosData, null, null);
        if (posData == null)
            return null;

        const int jpegQuality = 85;
        Cv2.ImEncode(".jpg", left, out var leftBuf, [(int)ImwriteFlags.JpegQuality, jpegQuality]);
        Cv2.ImEncode(".jpg", right, out var rightBuf, [(int)ImwriteFlags.JpegQuality, jpegQuality]);

        var header = GenerateHeader(posData);

        var frame = new Frame
        {
            Header = header with
            {
                JpegDataLeftLength = (uint)leftBuf.Length,
                JpegDataRightLength = (uint)rightBuf.Length
            },
            LeftJpeg = leftBuf,
            RightJpeg = rightBuf
        };

        lock (_lock)
        {
            _frames.Add(frame);
        }

        return frame;
    }

    public void WriteBin(string path)
    {
        List<Frame> copy;
        lock (_lock)
        {
            copy = new List<Frame>(_frames);
            _frames.Clear();
        }

        CaptureBin.IO.CaptureBin.WriteAll(Path.Combine(Utils.ModelDataDirectory, path), copy);
    }
}
public class BinCollector
{
    private readonly object _lock = new();
    private readonly List<Frame> _frames = new();
    private uint _headerFlags;
    // empty data, because bin
    HmdPositionalDataPacket positionalData = new HmdPositionalDataPacket();

    public BinCollector(uint headerFlags)
    {
        _headerFlags = headerFlags;
    }


    private CaptureFrameHeader GenerateHeader()
    {
        var time = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();
        return new CaptureFrameHeader
        {
            RoutineState = _headerFlags,
            LeftEyePitch = positionalData.LeftEyePitch,
            LeftEyeYaw = positionalData.LeftEyeYaw,
            RightEyePitch = positionalData.RightEyePitch,
            RightEyeYaw = positionalData.RightEyeYaw,
            RoutinePitch = positionalData.RoutinePitch,
            RoutineYaw = positionalData.RoutineYaw,
            RoutineDistance = positionalData.RoutineDistance,
            RoutineConvergence = positionalData.RoutineConvergence,
            FovAdjustDistance = positionalData.FovAdjustDistance,
            Timestamp = time,
            TimestampLeft = time,
            TimestampRight = time,
        };
    }

    public Frame AddFrame(Mat left, Mat right)
    {
        const int jpegQuality = 85;
        Cv2.ImEncode(".jpg", left, out var leftBuf, [(int)ImwriteFlags.JpegQuality, jpegQuality]);
        Cv2.ImEncode(".jpg", right, out var rightBuf, [(int)ImwriteFlags.JpegQuality, jpegQuality]);

        var header = GenerateHeader();

        var frame = new Frame
        {
            Header = header with
            {
                JpegDataLeftLength = (uint)leftBuf.Length,
                JpegDataRightLength = (uint)rightBuf.Length
            },
            LeftJpeg = leftBuf,
            RightJpeg = rightBuf
        };

        lock (_lock)
        {
            _frames.Add(frame);
        }

        return frame;
    }

    public void WriteBin(string path)
    {
        List<Frame> copy;
        lock (_lock)
        {
            copy = new List<Frame>(_frames);
            _frames.Clear();
        }

        CaptureBin.IO.CaptureBin.WriteAll(Path.Combine(Utils.ModelDataDirectory, path), copy);
    }
}
