using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Baballonia.CaptureBin.IO;

/// <summary>
/// Struct layout for a single frame header in the custom .bin capture format.
/// Matches the native C++ struct defined in capture_data.h (packed, little-endian).
/// After this fixed-size header, two JPEG blobs follow: left then right, with lengths given below.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CaptureFrameHeader
{
    // Unified gaze
    public float RoutinePitch;        // degrees
    public float RoutineYaw;          // degrees
    public float RoutineDistance;     // meters
    public float RoutineConvergence;  // 0..1
    public float FovAdjustDistance;   // units

    // Per-eye gaze
    public float LeftEyePitch;        // degrees
    public float LeftEyeYaw;          // degrees
    public float RightEyePitch;       // degrees
    public float RightEyeYaw;         // degrees

    // Lid/brow attributes
    public float RoutineLeftLid;
    public float RoutineRightLid;
    public float RoutineBrowRaise;    // surprise
    public float RoutineBrowAngry;    // lower
    public float RoutineWiden;
    public float RoutineSquint;
    public float RoutineDilate;       // dilation stimulus

    // Timestamps (milliseconds)
    public ulong Timestamp;           // label timestamp
    public ulong TimestampLeft;       // left eye video timestamp
    public ulong TimestampRight;      // right eye video timestamp

    // State and payload sizes
    public uint RoutineState;         // flags (see flags.h)
    public uint JpegDataLeftLength;
    public uint JpegDataRightLength;
}

/// <summary>
/// Represents a decoded frame with header and JPEG payloads.
/// </summary>
public sealed class Frame
{
    private static FastCorruptionDetector.FastCorruptionDetector _fastCorruptionDetector = new();

    public CaptureFrameHeader Header;
    public byte[] LeftJpeg = [];
    public byte[] RightJpeg = [];

    /// <summary>
    /// Decode left JPEG to grayscale image. Caller owns returned <see cref="Mat"/>.
    /// </summary>
    public Mat DecodeLeftGray()
    {
        if (LeftJpeg.Length == 0) return new Mat();
        using var buf = Mat.FromPixelData(1, LeftJpeg.Length, MatType.CV_8UC1, LeftJpeg);
        var bgr = Cv2.ImDecode(buf, ImreadModes.Color);
        if (bgr.Empty()) return new Mat();
        var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    /// <summary>
    /// Decode right JPEG to grayscale image. Caller owns returned <see cref="Mat"/>.
    /// </summary>
    public Mat DecodeRightGray()
    {
        if (RightJpeg.Length == 0) return new Mat();
        using var buf = Mat.FromPixelData(1, RightJpeg.Length, MatType.CV_8UC1, RightJpeg);
        var bgr = Cv2.ImDecode(buf, ImreadModes.Color);
        if (bgr.Empty()) return new Mat();
        var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    // Frame corruption detection
    public (bool leftCorrutped, bool rightCorrupted) IsCorrupted()
    {
        using var l = DecodeLeftGray();
        using var r = DecodeRightGray();
        var pair = _fastCorruptionDetector.ProcessFramePair(l, r);
        return (pair.LeftCorrupted, pair.RightCorrupted);
    }
}
