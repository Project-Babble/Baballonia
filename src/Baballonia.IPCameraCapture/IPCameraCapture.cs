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

    // Resolve unfiltered (AF_UNSPEC) so mDNS ".local" names resolve the same way they do for browsers
    // and curl — a family-filtered (AF_INET) lookup fails to find them on many systems — then connect
    // IPv4-first to avoid stalling on an IPv6 link-local. A short ConnectTimeout bounds the open.
    private HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(4),
            ConnectCallback = async (context, ct) =>
            {
                var host = context.DnsEndPoint.Host;
                var addresses = await ResolveAsync(host, ct).ConfigureAwait(false);
                if (addresses.Length == 0)
                    throw new SocketException((int)SocketError.HostNotFound);

                var ordered = addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
                Logger.LogDebug("IP camera '{Host}' resolved to {Addresses}", host, string.Join(", ", ordered.Select(a => a.ToString())));

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(ordered, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        // No overall timeout — the stream is open-ended; ConnectTimeout above bounds the open.
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    // The OS resolver doesn't do mDNS ".local" inside sandboxed runtimes like the Steam Linux Runtime
    // (no nss-mdns/Avahi), so a ".local" lookup throws "Name or service not known" even though the
    // host shell's browser/curl resolve it. Fall back to an in-process mDNS query, which only needs
    // multicast UDP — available in the sandbox.
    private async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        var isLocal = host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            if (addresses.Length > 0 || !isLocal)
                return addresses;
        }
        catch (SocketException) when (isLocal)
        {
            // OS resolver can't see mDNS here — fall through.
        }

        var viaMdns = await ResolveMdnsAsync(host, ct).ConfigureAwait(false);
        if (viaMdns.Length > 0)
            Logger.LogDebug("IP camera '{Host}' resolved via mDNS", host);
        return viaMdns;
    }

    // Minimal one-shot multicast-DNS A-record lookup (RFC 6762): ask 224.0.0.251:5353 for the host's
    // IPv4 with the unicast-response bit set, and read the direct reply. Returns empty on timeout/error.
    private static async Task<IPAddress[]> ResolveMdnsAsync(string host, CancellationToken ct)
    {
        var name = host.TrimEnd('.');
        var results = new List<IPAddress>();
        try
        {
            using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.Bind(new IPEndPoint(IPAddress.Any, 0));
            var mdns = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            await udp.SendToAsync(BuildMdnsQuery(name), SocketFlags.None, mdns, cts.Token).ConfigureAwait(false);

            var buf = new byte[4096];
            while (results.Count == 0)
            {
                var n = await udp.ReceiveAsync(buf, SocketFlags.None, cts.Token).ConfigureAwait(false);
                ParseMdnsARecords(buf, n, results);
            }
        }
        catch (OperationCanceledException) { /* timed out */ }
        catch (Exception) { /* network error — treat as unresolved */ }

        return results.ToArray();
    }

    private static byte[] BuildMdnsQuery(string name)
    {
        using var ms = new MemoryStream();
        ms.Write([0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0]); // id=0, flags=0, qdcount=1
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }
        ms.WriteByte(0);            // end of QNAME
        ms.Write([0, 1]);           // QTYPE = A
        ms.Write([0x80, 1]);        // QCLASS = IN with unicast-response (QU) bit
        return ms.ToArray();
    }

    private static void ParseMdnsARecords(byte[] buf, int len, List<IPAddress> results)
    {
        if (len < 12) return;
        int qd = (buf[4] << 8) | buf[5];
        int an = (buf[6] << 8) | buf[7];
        var pos = 12;
        for (var i = 0; i < qd && pos < len; i++) { SkipName(buf, ref pos, len); pos += 4; }
        for (var i = 0; i < an && pos < len; i++)
        {
            SkipName(buf, ref pos, len);
            if (pos + 10 > len) return;
            int type = (buf[pos] << 8) | buf[pos + 1];
            int rdlen = (buf[pos + 8] << 8) | buf[pos + 9];
            pos += 10;
            if (type == 1 && rdlen == 4 && pos + 4 <= len)
                results.Add(new IPAddress(buf.AsSpan(pos, 4)));
            pos += rdlen;
        }
    }

    private static void SkipName(byte[] buf, ref int pos, int len)
    {
        while (pos < len)
        {
            int l = buf[pos];
            if (l == 0) { pos++; return; }
            if ((l & 0xC0) == 0xC0) { pos += 2; return; } // compression pointer ends the name
            pos += 1 + l;
        }
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
            Logger.LogDebug("IP camera '{Url}' connecting...", url);

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
                Logger.LogError(ex, "IP camera '{Url}' failed: {Reason}", url, Describe(ex));
        }
    }

    // Flatten the exception chain onto one line so the underlying cause (a SocketException, an HTTP
    // framing error, etc.) is visible even when the log sink doesn't render the exception object.
    private static string Describe(Exception ex)
    {
        var sb = new StringBuilder();
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append(" -> ");
            sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
        }
        return sb.ToString();
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
