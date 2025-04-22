using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Ports;
using System.Numerics;
using System.Threading.Tasks;
using OpenCvSharp;

namespace AvaloniaMiaDev.Services.Inference.Captures;

/// <summary>
/// Serial Camera capture class intended for use on Desktop platforms
/// Babble-board specific implementation, assumes a fixed camera size of 240x240
/// </summary>
public sealed class SerialCameraCapture(string portName) : Capture(portName), IDisposable
{
    public override uint FrameCount { get; protected set; }

    private const int BaudRate = 3000000;
    private const ulong EtvrHeader = 0xd8ff0000a1ffa0ff, EtvrHeaderMask = 0xffff0000ffffffff;
    private bool _isDisposed;

    private readonly SerialPort _serialPort = new()
    {
        PortName = portName,
        BaudRate = BaudRate,
        ReadTimeout = SerialPort.InfiniteTimeout,
    };

    public override string Url { get; set; } = null!;
    public override Mat RawMat { get; } = new();

    public override (int width, int height) Dimensions => (RawMat.Width, RawMat.Height);

    public override bool IsReady { get; protected set; }

    public override Task<bool> StartCapture()
    {
        try
        {
            _serialPort.Open();
            IsReady = true;
            DataLoop();
        }
        catch (Exception)
        {
            IsReady = false;
        }

        return Task.FromResult(IsReady);
    }

    public override Task<bool> StopCapture()
    {
        try
        {
            _serialPort.Close();
            IsReady = false;
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    private async void DataLoop()
    {
        byte[] buffer = new byte[2048];
        try
        {
            while (_serialPort.IsOpen)
            {
                Stream stream = _serialPort.BaseStream;
                for (int bufferPosition = 0; bufferPosition < sizeof(ulong);)
                    bufferPosition += await stream.ReadAsync(buffer, bufferPosition, sizeof(ulong) - bufferPosition);
                ulong header = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
                for (; (header & EtvrHeaderMask) != EtvrHeader; header = header >> 8 | (ulong)buffer[0] << 56)
                    while (await stream.ReadAsync(buffer, 0, 1) == 0) /**/;

                ushort jpegSize = (ushort)(header >> BitOperations.TrailingZeroCount(~EtvrHeaderMask));
                if (buffer.Length < jpegSize)
                    Array.Resize(ref buffer, jpegSize);

                BinaryPrimitives.WriteUInt16LittleEndian(buffer, 0xd8ff);
                for (int bufferPosition = 2; bufferPosition < jpegSize;)
                    bufferPosition += await stream.ReadAsync(buffer, bufferPosition, jpegSize - bufferPosition);
                var newFrame = Mat.FromImageData(buffer);
                // Only update the frame count if the image data has actually changed
                if (newFrame.Width > 0 && newFrame.Height > 0)
                {
                    newFrame.CopyTo(RawMat);
                    FrameCount++; // Increment frame count to indicate a new frame is available
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Handle when the device is unplugged
            await StopCapture();
            Dispose();

        }
        catch (Exception)
        {
            await StopCapture();
        }
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed) return;

        if (disposing)
        {
            StopCapture(); // xlinka 11/8/24: Ensure capture stops before disposing resources
            _serialPort?.Dispose(); // xlinka 11/8/24: Dispose of serial port if initialized
        }
        _isDisposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this); // xlinka 11/8/24: Suppress finalization as resources are now disposed
    }

    ~SerialCameraCapture()
    {
        Dispose(false);
    }
}
