using System.Runtime.InteropServices;

namespace Baballonia.CaptureBin.IO;

/// <summary>
/// Reader and writer for the custom Baballs capture .bin format.
/// Each record consists of a fixed-size <see cref="CaptureFrameHeader"/> followed by:
/// - Left JPEG payload (byte count = JpegDataLeftLength)
/// - Right JPEG payload (byte count = JpegDataRightLength)
/// </summary>
public static class CaptureBin
{
    private static readonly int HeaderSize = Marshal.SizeOf<CaptureFrameHeader>();

    /// <summary>
    /// Enumerate frames from a .bin stream. Stops at EOF or first malformed record.
    /// The stream must be readable and positioned at the beginning of the file or record.
    /// </summary>
    private static IEnumerable<Frame> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException(@"Stream not readable", nameof(stream));

        var headerBytes = new byte[HeaderSize];

        while (true)
        {
            var read = FillBuffer(stream, headerBytes);
            if (read == 0) yield break; // EOF cleanly at record boundary
            if (read < headerBytes.Length) yield break; // partial header – stop

            var header = BytesToStruct<CaptureFrameHeader>(headerBytes);

            // Basic sanity checks
            if (header.JpegDataLeftLength > 100 * 1024 * 1024u || header.JpegDataRightLength > 100 * 1024 * 1024u)
                yield break;

            var left = ReadBytes(stream, (int)header.JpegDataLeftLength);
            if (left == null) yield break;
            var right = ReadBytes(stream, (int)header.JpegDataRightLength);
            if (right == null) yield break;

            yield return new Frame
            {
                Header = header,
                LeftJpeg = left,
                RightJpeg = right
            };
        }
    }

    /// <summary>
    /// Write frames to a .bin stream. The stream must be writable.
    /// </summary>
    private static void Write(Stream stream, IEnumerable<Frame> frames)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(frames);
        if (!stream.CanWrite) throw new ArgumentException(@"Stream not writable", nameof(stream));
        Span<byte> headerBytes = stackalloc byte[HeaderSize];

        foreach (var f in frames)
        {
            var header = f.Header;
            header.JpegDataLeftLength = (uint)(f.LeftJpeg?.Length ?? 0);
            header.JpegDataRightLength = (uint)(f.RightJpeg?.Length ?? 0);

            MemoryMarshal.Write(headerBytes, in header);
            stream.Write(headerBytes);

            if (header.JpegDataLeftLength > 0 && f.LeftJpeg != null)
                stream.Write(f.LeftJpeg, 0, f.LeftJpeg.Length);
            if (header.JpegDataRightLength > 0 && f.RightJpeg != null)
                stream.Write(f.RightJpeg, 0, f.RightJpeg.Length);
        }
    }

    /// <summary>
    /// Convenience: read all frames from file path.
    /// </summary>
    public static List<Frame> ReadAll(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs).ToList();
    }

    /// <summary>
    /// Convenience: write all frames to file path (overwrites).
    /// </summary>
    public static void WriteAll(string path, IEnumerable<Frame> frames)
    {
        // this throws if for some reason there is no parent dir for "path"
        Directory.CreateDirectory(Directory.GetParent(path)!.ToString());
        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Write(fs, frames);
    }

    /// <summary>
    /// Concatenate multiple .bin files into a single output file by appending raw bytes in order.
    /// </summary>
    public static void Concatenate(string outputPath, params string[] inputPaths)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(inputPaths);
        if (inputPaths.Length == 0) throw new ArgumentException(@"No input files provided", nameof(inputPaths));

        using var outStream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 1024];

        foreach (var inPath in inputPaths)
        {
            if (string.IsNullOrWhiteSpace(inPath)) continue;
            using var inStream = File.OpenRead(inPath);
            int read;
            while ((read = inStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                outStream.Write(buffer, 0, read);
            }
        }
    }

    private static int FillBuffer(Stream s, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = s.Read(buffer.Slice(total));
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    private static T BytesToStruct<T>(byte[] data) where T : struct
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static byte[]? ReadBytes(Stream s, int length)
    {
        if (length == 0) return [];
        var data = new byte[length];
        int total = 0;
        while (total < length)
        {
            int n = s.Read(data, total, length - total);
            if (n <= 0) return null;
            total += n;
        }
        return data;
    }
}
