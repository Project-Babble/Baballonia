using System;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.Json;
using Baballonia.Services.Firmware;

namespace Baballonia.Services;

public class FirmwareService
{
    public event Action OnFirmwareUpdateStart;
    public event Action OnFirmwareUpdateComplete;
    public event Action<string> OnFirmwareUpdateError;

    private const string PYTHON_COMMAND = "python3";
    private const string ESPTOOL_COMMAND = "esptool";
    private const int DEFAULT_BAUD_RATE = 921600;

    /// <summary>
    /// Uploads firmware to an ESP32-S3 device using esptool.py via Python subprocess
    /// </summary>
    /// <param name="port">COM port where the device is connected</param>
    /// <param name="pathToFirmware">Path to the firmware file to upload</param>
    /// <param name="isWireless"></param>
    /// <param name="ssid"></param>
    /// <param name="token">Cancellation token</param>
    /// <param name="password"></param>
    /// <returns>A task representing the asynchronous operation</returns>
    public void UploadFirmwareAsync(string port, string pathToFirmware, bool isWireless, string ssid = "", string password = "", CancellationToken token = default)
    {
        try
        {
            // Check if Python is installed
            var hasPython = RunPythonScript(arguments: "--version");
            if (!hasPython)
            {
                OnFirmwareUpdateError?.Invoke("Python is not installed or not in PATH. Please install Python to continue.");
                return;
            }

            // Check if firmware file exists
            if (!File.Exists(pathToFirmware))
            {
                OnFirmwareUpdateError?.Invoke($"Firmware file not found: {pathToFirmware}");
                return;
            }

            // Check for updates to the esptool
            RunPythonScript(arguments: $"-m pip install {ESPTOOL_COMMAND}");

            // Notify start of firmware update
            OnFirmwareUpdateStart?.Invoke();

            // Create process to run esptool.py
            RunPythonScript(
                arguments:
                $"-m {ESPTOOL_COMMAND} --chip esp32-s3 --port {port} --baud {DEFAULT_BAUD_RATE} write_flash 0x0 \"{pathToFirmware}\"");

            if (isWireless)
            {
                SendWirelessCredentials(port, ssid, password);
            }

            // Firmware update completed successfully
            OnFirmwareUpdateComplete?.Invoke();
        }
        catch (Exception ex)
        {
            OnFirmwareUpdateError?.Invoke($"Firmware update failed: {ex.Message}");
        }
    }

    private void SendWirelessCredentials(string port, string ssid, string password, string hostname = MdnsData.DefaultHostName)
    {
        // Create payload
        Payload payload = new Payload
        {
            commands =
            [
                new Command
                {
                    command = "set_wifi",
                    data = new WifiData { ssid = ssid, password = password }
                }
            ]
        };

        if (!string.IsNullOrWhiteSpace(hostname))
        {
            payload.commands = payload.commands.Append(new Command
            {
                command = "set_mdns",
                data = new MdnsData { hostname = hostname }
            }).ToArray();
        }

        string jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        try
        {
            // Create a new SerialPort object with the specified port name and baud rate
            using SerialPort serialPort = new SerialPort(port, DEFAULT_BAUD_RATE);

            // Set serial port parameters
            serialPort.DataBits = 8;
            serialPort.StopBits = StopBits.One;
            serialPort.Parity = Parity.None;
            serialPort.Handshake = Handshake.None;

            // Set read/write timeouts
            serialPort.ReadTimeout = 2000;
            serialPort.WriteTimeout = 2000;

            try
            {
                // Open the port
                serialPort.Open();

                // Convert the payload to bytes
                byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

                // Write the payload to the serial port
                serialPort.Write(payloadBytes, 0, payloadBytes.Length);

                // Add a newline to indicate end of message
                serialPort.Write("\n");

                // Allow time for the device to process
                Thread.Sleep(500);

                // Check for response
                if (!(serialPort.BytesToRead > 0))
                {
                    OnFirmwareUpdateError?.Invoke($"Firmware update failed: No response from serial port");
                }
            }
            finally
            {
                // Close the port
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                }
            }
        }
        catch (Exception ex)
        {
            OnFirmwareUpdateError?.Invoke($"Firmware update failed: {ex.Message}");
        }
    }

    private bool RunPythonScript(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = PYTHON_COMMAND;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            OnFirmwareUpdateError?.Invoke($"Firmware update failed: {ex.Message}");
            return false;
        }
    }
}
