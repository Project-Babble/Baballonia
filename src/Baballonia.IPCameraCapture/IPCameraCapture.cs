using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.IPCameraCapture;

/// <summary>
/// Captures and decodes a known-size MJPEG stream, commonly used by IP Cameras
/// https://github.com/Larry57/SimpleMJPEGStreamViewer
/// https://stackoverflow.com/questions/3801275/how-to-convert-image-to-byte-array
/// </summary>
public sealed class IpCameraCapture(string url, ILogger<IpCameraCapture> logger) : Capture(url, logger)
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private HttpClient? _httpClient;

    // JPEG delimiters
    private const byte PicMarker = 0xFF;
    private const byte PicStart = 0xD8;
    private const byte PicEnd = 0xD9;

    public override Task<bool> StartCapture()
    {
        _httpClient = CreateHttpClient();
        _ = Task.Run(() => StartStreaming(_httpClient, Source, null, null, _cancellationTokenSource.Token));
        IsReady = true;
        return Task.FromResult(true);
    }

    // Prefer IPv4 (A-record) connects with a short timeout: a ".local" name resolving to an IPv6
    // link-local can stall the open past the caller's frame window, which wedges a 1-connection camera.
    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(4),
            ConnectCallback = async (context, ct) =>
            {
                var host = context.DnsEndPoint.Host;
                var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
                if (addresses.Length == 0)
                    addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        return new HttpClient(handler);
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
    private async Task StartStreaming(HttpClient cli, string url, string? login = null, string? password = null, CancellationToken? token = null,
        int chunkMaxSize = 1024, int frameBufferSize = 1024 * 1024)
    {
        var tok = token ?? CancellationToken.None;

        try
        {
            if (!string.IsNullOrEmpty(login) && !string.IsNullOrEmpty(password))
                cli.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{login}:{password}")));

            using var response = await cli.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, tok).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError("IP camera '{Url}' returned HTTP {Status}", url, (int)response.StatusCode);
                return;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!mediaType.Contains("multipart", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Contains("jpeg", StringComparison.OrdinalIgnoreCase))
                Logger.LogWarning("IP camera '{Url}' served '{ContentType}', not an MJPEG stream — check the address (port/path)", url, mediaType);
            else
                Logger.LogDebug("IP camera '{Url}' connected ({ContentType})", url, mediaType);

            using var stream = await response.Content.ReadAsStreamAsync(tok).ConfigureAwait(false);

            var streamBuffer = new byte[chunkMaxSize];
            var frameBuffer = new byte[frameBufferSize];
            var frameIdx = 0;
            var inPicture = false;
            byte current = 0x00;
            byte previous = 0x00;
            var loggedFirstFrame = false;

            while (!tok.IsCancellationRequested)
            {
                var streamLength = await stream.ReadAsync(streamBuffer.AsMemory(0, chunkMaxSize), tok).ConfigureAwait(false);
                if (streamLength == 0)
                {
                    Logger.LogWarning("IP camera '{Url}' closed the connection", url);
                    break;
                }
                ParseStreamBuffer(frameBuffer, ref frameIdx, streamLength, streamBuffer, ref inPicture, ref previous, ref current);

                if (!loggedFirstFrame && FramesProduced > 0)
                {
                    Logger.LogInformation("IP camera '{Url}' delivering frames", url);
                    loggedFirstFrame = true;
                }
            }
        }
        catch (OperationCanceledException) { /* normal on StopCapture */ }
        catch (Exception ex)
        {
            // A forced teardown (StopCapture disposes the client) surfaces here as a disposed/aborted
            // request rather than cancellation — don't report that as a real failure.
            if (tok.IsCancellationRequested)
                Logger.LogDebug("IP camera '{Url}' stream stopped", url);
            else
                Logger.LogError(ex, "IP camera '{Url}' connection failed", url);
        }
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
        IsReady = false;
        _cancellationTokenSource.Cancel();
        // Dispose, not just cancel: a stuck socket read may ignore the token; disposing aborts it and
        // frees the single-connection camera's slot so failover/retry can connect. Non-blocking.
        _httpClient?.Dispose();
        return Task.FromResult(true);
    }

    public override void Dispose() => StopCapture();

    private static byte[] TrimEnd(byte[] array)
    {
        int lastIndex = Array.FindLastIndex(array, b => b != 0);

        Array.Resize(ref array, lastIndex + 1);

        return array;
    }
}
