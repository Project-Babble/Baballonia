using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using Baballonia.Contracts;

namespace Baballonia.Helpers
{
    public class SerialCommandSender : ICommandSender
    {
        private const int DefaultBaudRate = 115200; // esptool-rs: Setting baud rate higher than 115,200 can cause issues
        private readonly SerialPort _serialPort;

        public SerialCommandSender(string port)
        {
            _serialPort = new SerialPort(port, DefaultBaudRate);

            // Set serial port parameters
            _serialPort.DataBits = 8;
            _serialPort.StopBits = StopBits.One;
            _serialPort.Parity = Parity.None;
            _serialPort.Handshake = Handshake.None;

            // Set read/write timeouts
            _serialPort.ReadTimeout = 30000;
            _serialPort.WriteTimeout = 30000;
            _serialPort.Encoding = Encoding.UTF8;

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
                catch (IOException)
                {
                    // Timeout
                    maxRetries = 0;
                }
                catch (Exception ex)
                {
                    if (ex is not FileNotFoundException && ex is not UnauthorizedAccessException) throw;
                    maxRetries--;
                    Thread.Sleep(sleepTimeInMs);
                }
            }
        }

        public void Dispose()
        {
            if (_serialPort.IsOpen)
                _serialPort.Close();

            _serialPort.Dispose();
        }

        public string ReadLine(TimeSpan timeout)
        {
            StringBuilder responseBuilder = new StringBuilder();
            var startTime = DateTime.Now;
            do
            {
                if (DateTime.Now - startTime > timeout)
                    throw new TimeoutException("Timeout reached");

                // Read available data
                if (_serialPort.BytesToRead > 0)
                {
                    string receivedData = _serialPort.ReadExisting();
                    responseBuilder.Append(receivedData);
                }
                else
                {
                    // Small delay to prevent CPU spinning
                    Thread.Sleep(10);
                }
            } while (responseBuilder.Length == 0);

            return responseBuilder.ToString().Trim();
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
}
