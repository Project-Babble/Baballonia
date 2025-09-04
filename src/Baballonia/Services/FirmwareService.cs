using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Models;
using Baballonia.Services.Firmware;
using MeaMod.DNS.Server;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class FirmwareService(ILogger<FirmwareService> logger, ICommandSenderFactory commandSenderFactory)
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

    public FirmwareSession StartSession(CommandSenderType type, string port)
    {
        return new FirmwareSession(commandSenderFactory.Create(type, port), logger);
    }

    /// <summary>
    /// Uploads firmware to an ESP32-S3 device using a subprocess esptool-rs
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

    public async Task<string[]> ProbeComPortsAsync(TimeSpan timeout)
    {
        var ports = FindAvailableComPorts();
        var goodPorts = new List<string>();
        var tasks = new ConcurrentSet<Task>();

        foreach (var port in ports)
        {
            tasks.Add(Task.Run(() =>
            {
                var session = StartSession(CommandSenderType.Serial, port);
                try
                {
                    logger.LogInformation("Probing {Port}", port);
                    var heartbeat = session.WaitForHeartbeat(timeout);
                    if (heartbeat != null)
                    {
                        lock (goodPorts) // protect against race conditions
                        {
                            goodPorts.Add(port);
                        }
                    }
                }
                catch (TimeoutException)
                {
                    logger.LogInformation("Probing port {Port}: timeout reached", port);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error probing port {Port}", port);
                }
                finally
                {
                    session.Dispose();
                }
            }));
        }

        await Task.WhenAll(tasks);
        return goodPorts.ToArray();
    }



    public string[] ProbeComPorts(TimeSpan timeout)
    {
        var ports = FindAvailableComPorts();
        List<string> goodPorts = [];
        foreach (var port in ports)
        {
            var session = StartSession(CommandSenderType.Serial, port);
            try
            {
                logger.LogInformation("Probing {}", port);
                var heartbeat = session.WaitForHeartbeat(timeout);
                if (heartbeat != null)
                {
                    goodPorts.Add(port);
                }

                session.Dispose();
            }
            catch (TimeoutException ex)
            {
                logger.LogInformation("probing port {}: timeout reached", port);
            }
            catch (Exception ex)
            {
                logger.LogError("Error probing port {}: {}", port, ex.Message);
            }
            finally
            {
                session.Dispose();
            }
        }

        return [.. goodPorts];
    }

    private static string[] FindAvailableComPorts()
    {
        // GetPortNames() may return single port multiple times
        // https://stackoverflow.com/questions/33401217/serialport-getportnames-returns-same-port-multiple-times
        return SerialPort.GetPortNames().Distinct().ToArray();
    }
}

