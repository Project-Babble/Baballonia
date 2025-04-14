using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace AvaloniaMiaDev.Services.Inference;

public class CameraStreamServer : IDisposable
{
    private readonly object _streamLock = new();
    private readonly int _port;
    private readonly string _boundary;
    private byte[] _currentFrame;
    private bool _isStreaming;
    private CancellationTokenSource _cancellationTokenSource;
    private TcpListener _tcpListener;
    private UdpClient _udpClient;

    public CameraStreamServer(int port = 8080, string boundary = "camerastream")
    {
        _port = port;
        _boundary = boundary;
        _currentFrame = [];
    }

    public void StartStreaming()
    {
        if (_isStreaming)
            return;

        _isStreaming = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            // Start UDP server for video streaming
            _udpClient = new UdpClient(_port);
            Task.Run(() => HandleUdpStreaming(_cancellationTokenSource.Token));
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
        _cancellationTokenSource?.Cancel();

        try
        {
            _tcpListener?.Stop();
            _udpClient?.Close();
        }
        catch (Exception)
        {
            // Ignore cleanup errors
        }
    }

    public void UpdateFrame(SKImage frameData)
    {
        if (!_isStreaming)
            return;

        lock (_streamLock)
        {
            _currentFrame = frameData.Encode(SKEncodedImageFormat.Jpeg, quality: 80).ToArray();
        }
    }

    private async Task HandleUdpStreaming(CancellationToken cancellationToken)
    {
        while (_isStreaming && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                byte[] frameData;
                lock (_streamLock)
                {
                    frameData = _currentFrame;
                }

                if (frameData.Length > 0)
                {
                    // Send frame data over UDP
                    // We'll use a simple protocol where the first 4 bytes are the frame length
                    var frameLength = BitConverter.GetBytes(frameData.Length);
                    var packet = new byte[frameData.Length + 4];
                    Buffer.BlockCopy(frameLength, 0, packet, 0, 4);
                    Buffer.BlockCopy(frameData, 0, packet, 4, frameData.Length);

                    await _udpClient.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, _port));
                }

                await Task.Delay(33, cancellationToken); // ~30 FPS
            }
            catch (Exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    // Handle streaming errors
                }
            }
        }
    }

    public void Dispose()
    {
        StopStreaming();
        _cancellationTokenSource?.Dispose();
        _tcpListener?.Stop();
        _udpClient?.Close();
    }
}
