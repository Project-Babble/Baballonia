using OpenCvSharp;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.Services;

public class MjpegStreamingService : IDisposable
{
    private HttpListener _httpListener;
    private bool _isStreaming;
    private CancellationTokenSource _streamingCancellationTokenSource;
    private readonly object _streamLock = new();
    private byte[] _currentJpegFrame;
    private readonly string _mjpegBoundary = "mjpegstream";

    public void StartStreaming(int port)
    {
        if (_isStreaming)
            return;

        _isStreaming = true;
        _streamingCancellationTokenSource = new CancellationTokenSource();

        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{port}/");
            _httpListener.Start();

            Task.Run(() => HandleHttpRequests(_streamingCancellationTokenSource.Token));
        }
        catch (Exception)
        {
            _isStreaming = false;
        }

    }
    public void StopStreaming()
    {
        if (!_isStreaming)
            return;

        _isStreaming = false;
        _streamingCancellationTokenSource?.Cancel();

        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
        catch (Exception)
        {
            // ignored
        }
    }
    public void UpdateMjpegFrame(Mat mat)
    {
        if (!_isStreaming)
            return;

        try
        {
            // Update the current frame
            lock (_streamLock)
            {
                _currentJpegFrame = mat.ToBytes(ext: ".jpg"); // Cv2.Imencode
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private async Task HandleHttpRequests(CancellationToken cancellationToken)
    {
        while (_isStreaming && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();

                if (cancellationToken.IsCancellationRequested)
                    break;

                string requestPath = context.Request.Url!.AbsolutePath.ToLowerInvariant();

#pragma warning disable CS4014 // Awaiting prevents the calibration app from receiving MJPEG frames
                if (requestPath == "/mjpeg")
                {
                    // Handle MJPEG stream request
                    Task.Run(() => HandleMjpegRequest(context, cancellationToken));
                }
                else if (requestPath == "/snapshot" || requestPath == "/jpeg")
                {
                    // Handle single JPEG snapshot request
                    Task.Run(() => HandleSnapshotRequest(context));
                }
                else
                {
                    // Handle other requests (like a simple status page)
                    HandleDefaultRequest(context);
                }
#pragma warning restore CS4014 // Awaiting prevents the calibration app from receiving MJPEG frames
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
    private async Task HandleMjpegRequest(HttpListenerContext context, CancellationToken cancellationToken)
    {
        // Get the underlying TCP connection
        var response = context.Response;

        try
        {
            // Write the HTTP response headers manually
            string responseHeaders =
                "HTTP/1.1 200 OK\r\n" +
                $"Content-Type: multipart/x-mixed-replace; boundary={_mjpegBoundary}\r\n" +
                "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
                "Pragma: no-cache\r\n" +
                "Expires: 0\r\n" +
                "Connection: close\r\n\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(responseHeaders);

            // Get the raw output stream
            using var outputStream = response.OutputStream;
            await outputStream.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken);

            // Initial boundary
            string initialBoundary = $"--{_mjpegBoundary}\r\n";
            byte[] boundaryBytes = Encoding.ASCII.GetBytes(initialBoundary);
            await outputStream.WriteAsync(boundaryBytes, 0, boundaryBytes.Length, cancellationToken);

            // Stream frames
            while (_isStreaming && !cancellationToken.IsCancellationRequested)
            {
                if (_currentJpegFrame == null || _currentJpegFrame.Length == 0)
                {
                    await Task.Delay(33, cancellationToken);
                    continue;
                }

                // Copy to avoid race conditions
                byte[] frameData = _currentJpegFrame;

                try
                {
                    // Frame headers
                    string frameHeader = "Content-Type: image/jpeg\r\n" +
                                         $"Content-Length: {frameData.Length}\r\n" +
                                         $"X-Timestamp: {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}\r\n\r\n";
                    byte[] frameHeaderBytes = Encoding.ASCII.GetBytes(frameHeader);

                    await outputStream.WriteAsync(frameHeaderBytes, 0, frameHeaderBytes.Length, cancellationToken);
                    await outputStream.WriteAsync(frameData, 0, frameData.Length, cancellationToken);

                    // Next boundary
                    string nextBoundary = $"\r\n--{_mjpegBoundary}\r\n";
                    byte[] nextBoundaryBytes = Encoding.ASCII.GetBytes(nextBoundary);
                    await outputStream.WriteAsync(nextBoundaryBytes, 0, nextBoundaryBytes.Length, cancellationToken);

                    await outputStream.FlushAsync(cancellationToken);
                }
                catch (IOException)
                {
                    break;
                }

                await Task.Delay(33, cancellationToken);
            }
        }
        catch (Exception)
        {
            // ignored
        }
        finally
        {
            try { response.Abort(); }
            catch
            {
                // ignored
            }
        }
    }
    private void HandleSnapshotRequest(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;

        try
        {
            response.ContentType = "image/jpeg";
            response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
            response.Headers.Add("Pragma", "no-cache");
            response.Headers.Add("Expires", "0");

            // Copy to avoid race conditions
            byte[] frameData = _currentJpegFrame;

            if (frameData.Length > 0)
            {
                //response.ContentLength64 = frameData.Length;
                response.OutputStream.Write(frameData, 0, frameData.Length);
            }
            else
            {
                response.StatusCode = 503; // Service Unavailable
                byte[] errorMsg = Encoding.UTF8.GetBytes("No image available");
                //response.ContentLength64 = errorMsg.Length;
                response.OutputStream.Write(errorMsg, 0, errorMsg.Length);
            }
        }
        catch (Exception)
        {
            // ignored
        }
        finally
        {
            response.Close();
        }
    }

    private void HandleDefaultRequest(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;

        try
        {
            string html = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Camera Stream</title>
                    <style>
                        body {{ font-family: Arial, sans-serif; margin: 20px; }}
                        h1 {{ color: #333; }}
                        .stream-container {{ margin-top: 20px; }}
                        img {{ max-width: 100%; border: 1px solid #ccc; }}
                    </style>
                </head>
                <body>
                    <h1>Camera Stream</h1>
                    <div class='stream-container'>
                        <h2>Live Stream</h2>
                        <img src='/mjpeg' alt='MJPEG Stream'>
                    </div>
                    <div class='stream-container'>
                        <h2>Static Image</h2>
                        <img src='/snapshot' alt='Snapshot'>
                    </div>
                </body>
                </html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html";
            //response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception)
        {
            // ignored
        }
        finally
        {
            response.Close();
        }
    }

    public void Dispose()
    {
        StopStreaming();
        _httpListener?.Close();
        _streamingCancellationTokenSource?.Dispose();
    }
}
