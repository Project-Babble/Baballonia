using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Baballonia.CaptureBin.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenCvSharp;

namespace Baballonia.Tests.OverlayTests;

[TestClass]
public class CaptureBinDecodeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Decode_UserCalBin_And_Print_Output()
    {
        var baseDir = AppContext.BaseDirectory;
        var binPath = Path.Combine(baseDir, "OverlayTests", "user_cal.bin");
        if (!File.Exists(binPath))
        {
            Assert.Fail($"Test data not found at: {binPath}");
        }

        var frames = CaptureBin.IO.CaptureBin.ReadAll(binPath);
        TestContext.WriteLine($"Total frames: {frames.Count}");

        var take = Math.Min(5, frames.Count);
        for (var i = 0; i < take; i++)
        {
            var f = frames[i];

            // Print header values
            var h = f.Header;
            TestContext.WriteLine($"Frame {i}:");
            TestContext.WriteLine($"  Unified: Pitch={h.RoutinePitch:F3}°, Yaw={h.RoutineYaw:F3}°, Dist={h.RoutineDistance:F3} m, Conv={h.RoutineConvergence:F3}");
            TestContext.WriteLine($"  Per-eye: L(P={h.LeftEyePitch:F3}°, Y={h.LeftEyeYaw:F3}°), R(P={h.RightEyePitch:F3}°, Y={h.RightEyeYaw:F3}°)");
            TestContext.WriteLine($"  Lids/Brow: LLid={h.RoutineLeftLid:F3}, RLid={h.RoutineRightLid:F3}, Raise={h.RoutineBrowRaise:F3}, Angry={h.RoutineBrowAngry:F3}, Widen={h.RoutineWiden:F3}, Squint={h.RoutineSquint:F3}, Dilate={h.RoutineDilate:F3}");
            TestContext.WriteLine($"  Timestamps: Label={h.Timestamp} ms, Left={h.TimestampLeft} ms, Right={h.TimestampRight} ms");
            TestContext.WriteLine($"  State=0x{h.RoutineState:X8}, LeftJpeg={h.JpegDataLeftLength} bytes, RightJpeg={h.JpegDataRightLength} bytes");

            // Decode to verify images are valid JPEGs and print sizes
            using var left = f.DecodeLeftGray();
            using var right = f.DecodeRightGray();
            TestContext.WriteLine($"  Images: Left=({left.Width}x{left.Height}), Right=({right.Width}x{right.Height})");

            var (lcor, rcor) = f.IsCorrupted();
            TestContext.WriteLine($"  Corruption: Left={lcor}, Right={rcor}");
        }

        // Basic sanity assertions
        Assert.IsTrue(frames.Count > 0, "Expected at least one frame in user_cal.bin");
        Assert.IsTrue(frames.All(fr => fr.LeftJpeg.Length == fr.Header.JpegDataLeftLength), "Left JPEG length mismatch");
        Assert.IsTrue(frames.All(fr => fr.RightJpeg.Length == fr.Header.JpegDataRightLength), "Right JPEG length mismatch");
    }

    [TestMethod]
    public void Generate_UserCalBin_WithRandomFrames()
    {
        var baseDir = AppContext.BaseDirectory;
        var outDir = Path.Combine(baseDir, "OverlayTests");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "user_cal.generated.bin");

        var frames = new List<Frame>();
        var rng = new Random();

        var frameCount = 8000;
        var baseTs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var markRoutineComplete = true;

        for (var i = 0; i < frameCount; i++)
        {
            var header = new CaptureFrameHeader
            {
                RoutinePitch = (float)(rng.NextDouble() * 60 - 30),
                RoutineYaw = (float)(rng.NextDouble() * 60 - 30),
                RoutineDistance = (float)(0.4 + rng.NextDouble() * 0.6),
                RoutineConvergence = (float)rng.NextDouble(),
                FovAdjustDistance = (float)(rng.NextDouble() * 5.0),

                LeftEyePitch = (float)(rng.NextDouble() * 60 - 30),
                LeftEyeYaw = (float)(rng.NextDouble() * 60 - 30),
                RightEyePitch = (float)(rng.NextDouble() * 60 - 30),
                RightEyeYaw = (float)(rng.NextDouble() * 60 - 30),

                RoutineLeftLid = (float)rng.NextDouble(),
                RoutineRightLid = (float)rng.NextDouble(),
                RoutineBrowRaise = (float)rng.NextDouble(),
                RoutineBrowAngry = (float)rng.NextDouble(),
                RoutineWiden = (float)rng.NextDouble(),
                RoutineSquint = (float)rng.NextDouble(),
                RoutineDilate = (float)rng.NextDouble(),

                Timestamp = baseTs + (ulong)(i * 10),
                TimestampLeft = baseTs + (ulong)(i * 10 + 1),
                TimestampRight = baseTs + (ulong)(i * 10 + 2),

                // First frame: RESTING. Subsequent: IN_MOVEMENT | GOOD_DATA.
                // If last frame and markRoutineComplete is true: ROUTINE_COMPLETE instead.
                RoutineState = i == 0
                    ? CaptureFlags.FLAG_RESTING
                    : (i == frameCount - 1 && markRoutineComplete
                        ? CaptureFlags.FLAG_ROUTINE_COMPLETE
                        : (CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_GOOD_DATA)),
                JpegDataLeftLength = 0,
                JpegDataRightLength = 0
            };

            var leftJpeg = CreateRandomJpeg(i, 256, 256);
            var rightJpeg = CreateRandomJpeg(i, 256, 256);

            frames.Add(new Frame
            {
                Header = header,
                LeftJpeg = leftJpeg,
                RightJpeg = rightJpeg
            });
        }

        CaptureBin.IO.CaptureBin.WriteAll(outPath, frames);
        TestContext.WriteLine($"Generated bin: {outPath}");

        Assert.IsTrue(File.Exists(outPath), "Generated file not found");
        Assert.IsTrue(new FileInfo(outPath).Length > 0, "Generated file is empty");

        // Quickly read back first frame to ensure integrity
        var readBack = CaptureBin.IO.CaptureBin.ReadAll(outPath);
        Assert.AreEqual(frameCount, readBack.Count, "Frame count mismatch after roundtrip");
        using var lg = readBack[0].DecodeLeftGray();
        using var rg = readBack[0].DecodeRightGray();
        Assert.IsTrue(lg is { Width: > 0, Height: > 0 }, "Left image failed to decode");
        Assert.IsTrue(rg is { Width: > 0, Height: > 0 }, "Right image failed to decode");

        // Verify flags pattern
        Assert.AreEqual(CaptureFlags.FLAG_RESTING, readBack[0].Header.RoutineState, "First frame must be RESTING");
        var expectedLast = (markRoutineComplete ? CaptureFlags.FLAG_ROUTINE_COMPLETE : (CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_GOOD_DATA));
        Assert.AreEqual(expectedLast, readBack[^1].Header.RoutineState, "Last frame flag mismatch");
        for (var j = 1; j < readBack.Count - 1; j++)
        {
            Assert.AreEqual(CaptureFlags.FLAG_IN_MOVEMENT | CaptureFlags.FLAG_GOOD_DATA, readBack[j].Header.RoutineState, $"Frame {j} must be IN_MOVEMENT | GOOD_DATA");
        }
    }

    private static byte[] CreateRandomJpeg(int frameNumber, int width, int height)
    {
        using var img = new Mat(height, width, MatType.CV_8UC1);
        Cv2.Randu(img, Scalar.All(0), Scalar.All(255));
        Cv2.PutText(img,
            frameNumber.ToString(),
            new Point(10, height / 2),
            HersheyFonts.HersheySimplex,
            1.0,
            new Scalar(255, 255, 255),
            2);
        Cv2.ImEncode(".jpg", img, out var buf, [(int)ImwriteFlags.JpegQuality, 50]);
        return buf.ToArray();
    }
}
