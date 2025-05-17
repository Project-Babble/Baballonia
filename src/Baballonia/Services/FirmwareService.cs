using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Baballonia.Services;

public class FirmwareService
{
    public event Action OnFirmwareUpdateStart;
    public event Action<float> OnFirmwareUpdateProgress;
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
    /// <param name="token">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task UploadFirmwareAsync(string port, string pathToFirmware, CancellationToken token = default)
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

            // Firmware update completed successfully
            OnFirmwareUpdateComplete?.Invoke();
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
