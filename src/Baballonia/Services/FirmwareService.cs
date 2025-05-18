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

    private static readonly string EsptoolCommand;
    private const int DefaultBaudRate = 921600; // esptool-rs: Setting baud rate higher than 115,200 can cause issues

    static FirmwareService()
    {
        EsptoolCommand = OperatingSystem.IsWindows() ? "espflash.exe" : "espflash";
    }

    /// <summary>
    /// Uploads firmware to an ESP32-S3 device using an subprocess esptool-rs
    /// </summary>
    /// <param name="port">COM port where the device is connected</param>
    /// <param name="pathToFirmware">Path to the firmware file to upload</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public void UploadFirmware(string port, string pathToFirmware)
    {
        try
        {
            // Check if firmware file exists
            if (!File.Exists(pathToFirmware))
            {
                OnFirmwareUpdateError($"Firmware file not found: {pathToFirmware}");
                return;
            }

            // Notify start of firmware update
            OnFirmwareUpdateStart();

            // Create process to run esptool.py
            if (!RunEspSubprocess(
                    arguments:
                    $"write-bin 0x00 \"{pathToFirmware}\" --port {port} --baud {DefaultBaudRate}"))
            {
                OnFirmwareUpdateError($"Firmware update failed!");
            }

            // Wired firmware update completed successfully
            OnFirmwareUpdateComplete();
        }
        catch (Exception ex)
        {
            OnFirmwareUpdateError?.Invoke($"Firmware update failed: {ex.Message}");
        }
    }

    public void SendWirelessCredentials(string port, string ssid, string password, string hostname = MdnsData.DefaultHostName)
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
            WriteIndented = false
        });

        try
        {
            // Create a new SerialPort object with the specified port name and baud rate
            using SerialPort serialPort = new SerialPort(port, DefaultBaudRate);

            // Set serial port parameters
            serialPort.DataBits = 8;
            serialPort.StopBits = StopBits.One;
            serialPort.Parity = Parity.None;
            serialPort.Handshake = Handshake.None;

            // Set read/write timeouts
            serialPort.ReadTimeout = 5000;
            serialPort.WriteTimeout = 5000;

            try
            {
                // Open the port
                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();

                // Convert the payload to bytes
                byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

                // Write the payload to the serial port
                const int chunkSize = 64;
                for (int i = 0; i < payloadBytes.Length; i += chunkSize)
                {
                    int length = Math.Min(chunkSize, payloadBytes.Length - i);
                    serialPort.Write(payloadBytes, i, length);
                    Thread.Sleep(50); // Small pause between chunks
                }

                // Add a newline to indicate end of message
                serialPort.Write("\n");

                Thread.Sleep(1000);
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
            OnFirmwareUpdateError($"Firmware update failed: {ex.Message}");
        }
    }

    private bool RunEspSubprocess(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = EsptoolCommand;
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
            OnFirmwareUpdateError($"Firmware update failed: {ex.Message}");
            return false;
        }
    }
}
