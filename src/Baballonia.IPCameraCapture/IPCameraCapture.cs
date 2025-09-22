using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.IPCameraCapture;

/// <summary>
/// Captures and decodes a known-size MJPEG stream, commonly used by IP Cameras
/// https://github.com/Larry57/SimpleMJPEGStreamViewer
/// https://stackoverflow.com/questions/3801275/how-to-convert-image-to-byte-array
/// </summary>
public sealed class IpCameraCapture(string url, ILogger logger) : Capture(url, logger)
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    // JPEG delimiters
    private const byte PicMarker = 0xFF;
    private const byte PicStart = 0xD8;
    private const byte PicEnd = 0xD9;

    public override bool CanConnect(string connectionString)
    {
        return Uri.TryCreate(connectionString, UriKind.RelativeOrAbsolute, out _) && connectionString.StartsWith("http://");
    }

    public override Task<bool> StartCapture()
    {
        Task.Run(() => StartStreaming(Source, null, null, _cancellationTokenSource.Token)); // Size of Babble frame
        IsReady = true;
        return Task.FromResult(true);
    }

    /// <summary>
    /// Start a MJPEG on a http stream
    /// </summary>
    /// <param name="url">url of the http stream (only basic auth is implemented)</param>
    /// <param name="login">optional login</param>
    /// <param name="password">optional password (only basic auth is implemented)</param>
    /// <param name="token">cancellation token used to cancel the stream parsing</param>
    /// <param name="chunkMaxSize">Max chunk byte size when reading stream</param>
    /// <param name="frameBufferSize">Maximum frame byte size</param>
    /// <returns></returns>
    ///
    private async Task StartStreaming(string url, string? login = null, string? password = null, CancellationToken? token = null,
        int chunkMaxSize = 1024, int frameBufferSize = 1024 * 1024)
    {
        var tok = token ?? CancellationToken.None;

        using var cli = new HttpClient();

        if (!string.IsNullOrEmpty(login) && !string.IsNullOrEmpty(password))
            cli.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{login}:{password}")));

        using var stream = await cli.GetStreamAsync(url).ConfigureAwait(false);

        var streamBuffer = new byte[chunkMaxSize];      // Stream chunk read
        var frameBuffer = new byte[frameBufferSize];    // Frame buffer

        var frameIdx = 0;       // Last written byte location in the frame buffer
        var inPicture = false;  // Are we currently parsing a picture ?
        byte current = 0x00;    // The last byte read
        byte previous = 0x00;   // The byte before

        // Continuously pump the stream. The cancellation token is used to get out of there
        while (true)
        {
            var streamLength = await stream.ReadAsync(streamBuffer, 0, chunkMaxSize, tok).ConfigureAwait(false);
            ParseStreamBuffer(frameBuffer, ref frameIdx, streamLength, streamBuffer, ref inPicture, ref previous, ref current);
        };
    }

    // Parse the stream buffer

    private void ParseStreamBuffer(byte[] frameBuffer, ref int frameIdx, int streamLength, byte[] streamBuffer,
        ref bool inPicture, ref byte previous, ref byte current)
    {
        var idx = 0;
        while (idx < streamLength)
        {
            if (inPicture)
            {
                ParsePicture(frameBuffer, ref frameIdx, ref streamLength, streamBuffer, ref idx, ref inPicture,
                    ref previous, ref current);
            }
            else
            {
                SearchPicture(frameBuffer, ref frameIdx, ref streamLength, streamBuffer, ref idx, ref inPicture,
                    ref previous, ref current);
            }
        }
    }

    // While we are looking for a picture, look for a FFD8 (end of JPEG) sequence.
    private void SearchPicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer,
        ref int idx, ref bool inPicture, ref byte previous, ref byte current)
    {
        do
        {
            previous = current;
            current = streamBuffer[idx++];

            // JPEG picture start ?
            if (previous == PicMarker && current == PicStart)
            {
                frameIdx = 2;
                frameBuffer[0] = PicMarker;
                frameBuffer[1] = PicStart;
                inPicture = true;
                return;
            }
        } while (idx < streamLength);
    }

    // While we are parsing a picture, fill the frame buffer until a FFD9 is reach.
    private void ParsePicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer,
        ref int idx, ref bool inPicture, ref byte previous, ref byte current)
    {
        do
        {
            previous = current;
            current = streamBuffer[idx++];
            frameBuffer[frameIdx++] = current;

            // JPEG picture end ?
            if (previous == PicMarker && current == PicEnd)
            {
                // Using a memory stream this way prevent arrays copy and allocations
                using (var s = new MemoryStream(frameBuffer, 0, frameIdx))
                {
                    try
                    {
                        var mat = Mat.FromImageData(TrimEnd(frameBuffer));
                        SetRawMat(mat);
                    }
                    catch (Exception)
                    {
                        // We don't care about badly decoded pictures
                    }
                }

                inPicture = false;
                return;
            }
        } while (idx < streamLength);
    }

    public override Task<bool> StopCapture()
    {
        _cancellationTokenSource.Cancel();
        IsReady = false;
        return Task.FromResult(true);
    }

    private static byte[] TrimEnd(byte[] array)
    {
        int lastIndex = Array.FindLastIndex(array, b => b != 0);

        Array.Resize(ref array, lastIndex + 1);

        return array;
    }
}
