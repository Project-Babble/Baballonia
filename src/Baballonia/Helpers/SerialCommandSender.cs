using Baballonia.Contracts;
using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace Baballonia.Helpers;

public class SerialCommandSender : ICommandSender
{
    private const int DefaultBaudRate = 115200; // esptool-rs: Setting baud rate higher than 115,200 can cause issues
    private readonly SerialPort _serialPort;

    public SerialCommandSender(string port)
    {
        _serialPort = new SerialPort(port, DefaultBaudRate)
        {
            // Set serial port parameters
            DataBits = 8,
            StopBits = StopBits.One,
            Parity = Parity.None,
            Handshake = Handshake.None,

            // Set read/write timeouts
            ReadTimeout = 30000,
            WriteTimeout = 30000,
            Encoding = Encoding.UTF8
        };

        int maxRetries = 5;
        const int sleepTimeInMs = 50;
        while (maxRetries > 0)
        {
            try
            {
                _serialPort.Open();
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                break;
            }
            catch (Exception ex)
            {
                switch (ex)
                {
                    case FileNotFoundException:
                    case UnauthorizedAccessException:
                        maxRetries--;
                        Thread.Sleep(sleepTimeInMs);
                        break;

                    case IOException:
                    case InvalidOperationException:
                        maxRetries = 0;
                        break;

                    default:
                        throw;
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (_serialPort.IsOpen)
                _serialPort.Close();

            _serialPort.Dispose();
        }
        catch (IOException) { }
    }

    public string ReadLine(TimeSpan timeout)
    {
        string data;
        try
        {
            // Read available data
            if (_serialPort.BytesToRead > 0)
            {
                data = _serialPort.ReadExisting();
                data = data.Trim();
            }
            else
            {
                return "";
            }
        }
        catch (Exception ex)
        {
            switch (ex)
            {
                case IOException:
                case InvalidOperationException:
                case OperationCanceledException:
                    return ""; // Port is closed

                default:
                    throw;
            }
        }

        return data;
    }

    public void WriteLine(string payload)
    {
        _serialPort.DiscardInBuffer();

        // Convert the payload to bytes
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

        // Write the payload to the serial port
        const int chunkSize = 256;
        for (int i = 0; i < payloadBytes.Length; i += chunkSize)
        {
            int length = Math.Min(chunkSize, payloadBytes.Length - i);
            _serialPort.Write(payloadBytes, i, length);
            Thread.Sleep(50); // Small pause between chunks
        }
    }
}
