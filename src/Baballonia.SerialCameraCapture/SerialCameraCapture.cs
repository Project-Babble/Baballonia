using System.Buffers.Binary;
using System.IO.Ports;
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Capture = Baballonia.SDK.Capture;

namespace Baballonia.SerialCameraCapture;

/// <summary>
/// Serial Camera capture class intended for use on Desktop platforms
/// Babble-board specific implementation, assumes a fixed camera size of 240x240
/// </summary>
public sealed class SerialCameraCapture(string source, ILogger<SerialCameraCapture> logger) : Capture(source, logger)
{
    private const int BaudRate = 3000000;
    private const ulong EtvrHeader = 0xd8ff0000a1ffa0ff, EtvrHeaderMask = 0xffff0000ffffffff;
    private bool _isDisposed;

    private readonly SerialPort _serialPort = new()
    {
        PortName = source,
        BaudRate = BaudRate,
        ReadTimeout = SerialPort.InfiniteTimeout,
    };


    public override bool CanConnect(string connectionString)
    {
        var lowered = connectionString.ToLower();
        return lowered.StartsWith("com") ||
               lowered.StartsWith("/dev/tty") ||
               lowered.StartsWith("/dev/cu") ||
               lowered.StartsWith("/dev/serial/") ||
               lowered.StartsWith("/dev/ttyacm");
    }

    public override Task<bool> StartCapture()
    {
        Logger.LogDebug("Starting serial camera capture...");
        Logger.LogDebug("Port Name: '" + source + "'");

        try
        {
            Logger.LogDebug("Opening serial port '" + source + "'");
            _serialPort.Open();
            Logger.LogDebug("Serial port opened successfully");

            IsReady = true;
            Logger.LogDebug("Starting data loop for serial camera");
            DataLoop();

            Logger.LogDebug("Starting serial camera started!");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to start serial camera capture on port '" + source + "'");
            IsReady = false;
        }

        return Task.FromResult(IsReady);
    }

    private async void DataLoop()
    {
        Logger.LogDebug("Serial camera data loop started");
        var buffer = new byte[2048];
        try
        {
            while (_serialPort.IsOpen)
            {
                var stream = _serialPort.BaseStream;
                for (var bufferPosition = 0; bufferPosition < sizeof(ulong);) bufferPosition += await stream.ReadAsync(buffer, bufferPosition, sizeof(ulong) - bufferPosition);
                var header = BinaryPrimitives.ReadUInt64LittleEndian(buffer);

                for (; (header & EtvrHeaderMask) != EtvrHeader; header = header >> 8 | (ulong)buffer[0] << 56)
                    while (await stream.ReadAsync(buffer, 0, 1) == 0) /**/;

                var jpegSize = (ushort)(header >> BitOperations.TrailingZeroCount(~EtvrHeaderMask));
                if (buffer.Length < jpegSize)
                    Array.Resize(ref buffer, jpegSize);

                BinaryPrimitives.WriteUInt16LittleEndian(buffer, 0xd8ff);
                for (var bufferPosition = 2; bufferPosition < jpegSize;)
                    bufferPosition += await stream.ReadAsync(buffer, bufferPosition, jpegSize - bufferPosition);

                var last = BinaryPrimitives.ReadUInt16LittleEndian(buffer[(jpegSize - 2)..jpegSize]);
                if (last == 0xd9ff) //if the last two bytes arent the end of stream pattern, the image is corrupt
                {
                    var newFrame = Mat.FromImageData(buffer);
                    // Only update the frame count if the image data has actually changed
                    if (newFrame.Width > 0 && newFrame.Height > 0) SetRawMat(newFrame);
                    else newFrame.Dispose();
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Handle when the device is unplugged
            Logger.LogWarning("Serial port was disposed - device likely unplugged");
            await StopCapture();
            Dispose();

        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error stopping serial camera capture");
            await StopCapture();
        }
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

    private void Dispose(bool disposing)
    {
        if (_isDisposed) return;

        if (disposing)
        {
            Logger.LogDebug("Disposing serial camera capture resources...");
            StopCapture(); // xlinka 11/8/24: Ensure capture stops before disposing resources
            _serialPort?.Dispose(); // xlinka 11/8/24: Dispose of serial port if initialized
            Logger.LogDebug("Serial camera capture resources disposed");
        }
        _isDisposed = true;
    }

    public override void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this); // xlinka 11/8/24: Suppress finalization as resources are now disposed
    }

    ~SerialCameraCapture()
    {
        Dispose(false);
    }
}
