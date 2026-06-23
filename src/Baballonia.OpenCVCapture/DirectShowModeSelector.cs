#if WINDOWS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DirectShowLib;
using Microsoft.Extensions.Logging;

namespace Baballonia.OpenCVCapture;

/// <summary>
/// Picks a DirectShow capture format that OpenCV's DSHOW backend can actually stream.
///
/// The Windows DSHOW capture path (OpenCV's videoInput) cannot deliver Y800 (raw 8-bit
/// greyscale) frames - the device opens but no samples ever arrive. Many trackers expose
/// Y800 as their default pin, so we enumerate the device's modes, skip every Y800 mode,
/// and select the best of the rest. This is generic: no per-device assumptions, just
/// "drop Y800, prefer compressed, prefer larger/faster".
/// </summary>
internal static class DirectShowModeSelector
{
    // MEDIASUBTYPE_Y800 - raw 8-bit greyscale; unsupported by the OpenCV DSHOW capture path.
    private static readonly Guid MediaSubtypeY800 = new("30303859-0000-0010-8000-00aa00389b71");
    // MEDIASUBTYPE_MJPG - preferred replacement: compressed, USB-bandwidth friendly, widely supported.
    private static readonly Guid MediaSubtypeMjpg = new("47504a4d-0000-0010-8000-00aa00389b71");

    public readonly record struct Mode(string FourCc, int Width, int Height, double Fps);

    /// <summary>
    /// Returns the best non-Y800 mode for the device at <paramref name="deviceIndex"/>
    /// (same index ordering OpenCV/DSHOW uses), or null to leave OpenCV's defaults alone.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Mode? SelectBestSupportedMode(int deviceIndex, ILogger logger)
    {
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        if (deviceIndex < 0 || deviceIndex >= devices.Length)
            return null;

        var device = devices[deviceIndex];
        IFilterGraph2? graph = null;
        IBaseFilter? sourceFilter = null;
        IPin? pin = null;
        try
        {
            graph = (IFilterGraph2)new FilterGraph();
            graph.AddSourceFilterForMoniker(device.Mon, null, device.Name, out sourceFilter);
            if (sourceFilter == null)
                return null;

            pin = DsFindPin.ByCategory(sourceFilter, PinCategory.Capture, 0);
            if (pin is not IAMStreamConfig streamConfig)
                return null;

            streamConfig.GetNumberOfCapabilities(out int count, out int size);
            var capsPtr = Marshal.AllocCoTaskMem(size);
            var candidates = new List<(Guid subType, Mode mode)>();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (streamConfig.GetStreamCaps(i, out var mediaType, capsPtr) != 0 || mediaType == null)
                        continue;
                    try
                    {
                        if (mediaType.formatType != FormatType.VideoInfo || mediaType.formatPtr == IntPtr.Zero)
                            continue;
                        if (mediaType.subType == MediaSubtypeY800)
                            continue; // the whole point: skip Y800, DSHOW can't stream it through OpenCV
                        if (!TryGetFourCc(mediaType.subType, out var fourCc))
                            continue; // only formats we can re-select via OpenCV's FourCC property

                        var vih = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.formatPtr)!;
                        var scc = Marshal.PtrToStructure<VideoStreamConfigCaps>(capsPtr)!;
                        double fps = scc.MinFrameInterval > 0 ? 10_000_000.0 / scc.MinFrameInterval : 0;

                        candidates.Add((mediaType.subType,
                            new Mode(fourCc, vih.BmiHeader.Width, vih.BmiHeader.Height, fps)));
                    }
                    finally
                    {
                        DsUtils.FreeAMMediaType(mediaType);
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(capsPtr);
            }

            if (candidates.Count == 0)
            {
                logger.LogWarning(
                    "No non-Y800 DirectShow modes found for device index {Index}; leaving OpenCV defaults.",
                    deviceIndex);
                return null;
            }

            // Prefer MJPG, then larger frame area, then higher frame rate.
            candidates.Sort((a, b) =>
            {
                bool aMjpg = a.subType == MediaSubtypeMjpg, bMjpg = b.subType == MediaSubtypeMjpg;
                if (aMjpg != bMjpg) return bMjpg.CompareTo(aMjpg);
                long aArea = (long)a.mode.Width * a.mode.Height, bArea = (long)b.mode.Width * b.mode.Height;
                if (aArea != bArea) return bArea.CompareTo(aArea);
                return b.mode.Fps.CompareTo(a.mode.Fps);
            });

            var best = candidates[0].mode;
            logger.LogInformation(
                "Selected DirectShow mode {FourCc} {Width}x{Height}@{Fps:0}fps for device index {Index} (skipping Y800).",
                best.FourCc, best.Width, best.Height, best.Fps, deviceIndex);
            return best;
        }
        finally
        {
            if (pin != null) Marshal.ReleaseComObject(pin);
            if (sourceFilter != null) Marshal.ReleaseComObject(sourceFilter);
            if (graph != null) Marshal.ReleaseComObject(graph);
        }
    }

    /// <summary>
    /// FourCC-based media subtypes follow the GUID template
    /// {AABBCCDD-0000-0010-8000-00AA00389B71}, where AABBCCDD is the FourCC.
    /// Returns false for named subtypes (e.g. RGB24) that aren't addressable by FourCC.
    /// </summary>
    private static bool TryGetFourCc(Guid subType, out string fourCc)
    {
        fourCc = string.Empty;
        var b = subType.ToByteArray();
        ReadOnlySpan<byte> template = stackalloc byte[]
            { 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 };
        for (int i = 0; i < template.Length; i++)
            if (b[4 + i] != template[i]) return false;

        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            if (b[i] < 0x20 || b[i] > 0x7E) return false; // not printable ASCII -> not a usable FourCC
            chars[i] = (char)b[i];
        }
        fourCc = new string(chars);
        return true;
    }
}
#endif
