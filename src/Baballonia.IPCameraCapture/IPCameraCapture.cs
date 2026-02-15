using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.IPCameraCapture;

public sealed class IpCameraCapture(string url, ILogger<IpCameraCapture> logger) : Capture(url, logger)
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    // JPEG delimiters
    private const byte PicMarker = 0xFF;
    private const byte PicStart = 0xD8;
    private const byte PicEnd = 0xD9;

    // Timeout duration
    private readonly TimeSpan _readTimeout = TimeSpan.FromMilliseconds(500);

    public override Task<bool> StartCapture()
    {
        Task.Run(() => StartStreaming(Source, null, null, _cancellationTokenSource.Token));
        IsReady = true;
        return Task.FromResult(true);
    }

    private async Task StartStreaming(string url, string? login = null, string? password = null, CancellationToken? token = null,
        int chunkMaxSize = 1024, int frameBufferSize = 1024 * 1024)
    {
        var masterToken = token ?? CancellationToken.None;

        // OUTER LOOP: Keeps trying to connect/reconnect until the user stops capture
        while (!masterToken.IsCancellationRequested)
        {
            try
            {
                using var cli = new HttpClient();
                // Optimization: Set a conservative timeout on the client itself just in case headers hang
                cli.Timeout = TimeSpan.FromSeconds(1);

                if (!string.IsNullOrEmpty(login) && !string.IsNullOrEmpty(password))
                    cli.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{login}:{password}")));

                // Pass the master token here so we can cancel during the connection phase
                using var stream = await cli.GetStreamAsync(url, masterToken).ConfigureAwait(false);

                var streamBuffer = new byte[chunkMaxSize];
                var frameBuffer = new byte[frameBufferSize];

                var frameIdx = 0;
                var inPicture = false;
                byte current = 0x00;
                byte previous = 0x00;

                logger.LogInformation($"Connected to stream: {url}");

                // INNER LOOP: Pumps data
                while (!masterToken.IsCancellationRequested)
                {
                    // Create a timeout token specifically for this read operation
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(masterToken);
                    timeoutCts.CancelAfter(_readTimeout);

                    int streamLength;
                    try
                    {
                        // We await with the TIMEOUT token, not the master token
                        streamLength = await stream.ReadAsync(streamBuffer, 0, chunkMaxSize, timeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Check if the master token was cancelled (User requested stop)
                        if (masterToken.IsCancellationRequested)
                        {
                            return; // Exit gracefully
                        }

                        // Otherwise, it was our timeoutCts that fired
                        logger.LogWarning($"Stream read timed out ({_readTimeout.TotalMilliseconds}ms). Restarting capture...");
                        break; // Break the INNER loop to trigger the OUTER loop (reconnect)
                    }

                    // 0 bytes usually means the server closed the connection gracefully
                    if (streamLength == 0)
                    {
                        logger.LogWarning("Stream closed by server (0 bytes). Restarting...");
                        break;
                    }

                    ParseStreamBuffer(frameBuffer, ref frameIdx, streamLength, streamBuffer, ref inPicture, ref previous, ref current);
                }
            }
            catch (Exception ex) when (!masterToken.IsCancellationRequested)
            {
                // Catch network errors (ConnectionRefused, etc) preventing a crash
                logger.LogError(ex, "Error in stream capture. Retrying in 1 second...");

                // Wait a moment before reconnecting to avoid spamming a dead server
                try
                {
                    await Task.Delay(1000, masterToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    // ... Rest of the parsing logic (ParseStreamBuffer, SearchPicture, ParsePicture, etc.) remains exactly the same ...

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

    private void SearchPicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer,
        ref int idx, ref bool inPicture, ref byte previous, ref byte current)
    {
        do
        {
            previous = current;
            current = streamBuffer[idx++];

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

    private void ParsePicture(byte[] frameBuffer, ref int frameIdx, ref int streamLength, byte[] streamBuffer,
        ref int idx, ref bool inPicture, ref byte previous, ref byte current)
    {
        do
        {
            previous = current;
            current = streamBuffer[idx++];
            frameBuffer[frameIdx++] = current;

            if (previous == PicMarker && current == PicEnd)
            {
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

