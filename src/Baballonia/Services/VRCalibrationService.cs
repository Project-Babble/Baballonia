// VRCalibrationService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Baballonia.Models;
using Baballonia.Services.Overlay;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Baballonia.Services;

// Maybe this could be an abstract class and have overlay/trainer derive from it
public class VrCalibrationService : IVrService, IDisposable
{
    // Event to notify subscribers about process output, unused rn
    public event EventHandler<ProcessOutputEventArgs> ProcessOutputReceived;

    public static string Overlay { get; }
    public static string Trainer { get; }
    public static string OverlayPath { get; }

    private ILogger<VrCalibrationService> _logger;
    private Dictionary<string, Action<string>> _responseHandlers;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    private Dictionary<string, Process> _activeProcesses = new();

    static VrCalibrationService()
    {
        if (OperatingSystem.IsWindows())
        {
            OverlayPath = Path.Combine(AppContext.BaseDirectory, "Calibration", "Windows");
            Overlay = Path.Combine(OverlayPath, "gaze_overlay.exe");
            Trainer = Path.Combine(OverlayPath, "calibration_runner.exe");
        }
        else if (OperatingSystem.IsLinux())
        {
            OverlayPath = Path.Combine(AppContext.BaseDirectory, "Calibration", "Linux");
            Overlay = Path.Combine(OverlayPath, "gaze_overlay");
            Trainer = Path.Combine(OverlayPath, "calibration_runner");
        }
    }

    public VrCalibrationService(ILogger<VrCalibrationService> logger)
    {
        _logger = logger;
        _baseUrl = "http://localhost:23951";
        _httpClient = new HttpClient();

        _responseHandlers = new Dictionary<string, Action<string>>()
        {
            { "Error: (.*)", output => _logger.LogError(Regex.Match(output, "Error: (.*)").Groups[1].Value) },
        };
    }

    private async Task<bool> StartProcess(string program, string[] arguments = null, bool waitForExit = false, string[] blacklistedPrograms = null)
    {
        // Make sure program exists
        if (!File.Exists(program))
        {
            _logger.LogError($"Program not found: {program}");
            return false;
        }

        string processName = Path.GetFileNameWithoutExtension(program);

        // Make sure program isn't already running
        if (Process.GetProcesses().Any(p => p.ProcessName == processName))
        {
            _logger.LogInformation($"{processName} is already running");
            return true; // Already running is considered success
        }

        // Check if any blacklisted programs are running
        if (blacklistedPrograms != null && blacklistedPrograms.Length > 0)
        {
            foreach (var blacklisted in blacklistedPrograms)
            {
                if (Process.GetProcesses().Any(p => p.ProcessName == Path.GetFileNameWithoutExtension(blacklisted)))
                {
                    _logger.LogWarning($"Cannot start {processName} because {blacklisted} is running");
                    return false;
                }
            }
        }

        // Check if SteamVR is running. The VR programs need it!
        if (!Process.GetProcesses().Any(p => p.ProcessName.ToLower().Contains("vrserver")))
        {
            _logger.LogError("SteamVR is not running. Required for VR operations.");
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = program,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (arguments != null)
            startInfo.Arguments = string.Join(" ", arguments);

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += ProcessOutputHandler;
        process.ErrorDataReceived += ProcessErrorHandler;
        process.Exited += (_, _) =>
        {
            _activeProcesses.Remove(processName);
            _logger.LogInformation($"{processName} process exited");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _activeProcesses[processName] = process;
        _logger.LogInformation($"Successfully started {processName}");

        if (waitForExit)
            await process.WaitForExitAsync();

        _logger.LogInformation($"{processName} exited gracefully!");

        return true;
    }

    public async Task<bool> StartOverlay(string[] arguments = null, string[] blacklistedPrograms = null)
    {
        return await StartProcess(Overlay, arguments, waitForExit: false, blacklistedPrograms);
    }

    public async Task<bool> StartTrainer(string[] arguments = null, string[] blacklistedPrograms = null)
    {
        return await StartProcess(Trainer, arguments, waitForExit: true, blacklistedPrograms);
    }

    private bool StopProcess(string program)
    {
        string processName = Path.GetFileNameWithoutExtension(program);

        if (_activeProcesses.TryGetValue(processName, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(3000); // Wait up to 3 seconds for graceful exit
                }
                process.Dispose();
                _activeProcesses.Remove(processName);
                _logger.LogInformation($"Successfully stopped {processName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping {processName}: {ex.Message}");
                return false;
            }
        }
        else
        {
            // Check if it's running but not tracked by us
            var runningProcess = Process.GetProcesses().FirstOrDefault(p => p.ProcessName == processName);
            if (runningProcess != null)
            {
                try
                {
                    runningProcess.Kill();
                    runningProcess.WaitForExit(3000);
                    runningProcess.Dispose();
                    _logger.LogInformation($"Successfully stopped untracked {processName}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error stopping untracked {processName}: {ex.Message}");
                    return false;
                }
            }

            _logger.LogInformation($"{processName} is not running");
            return true; // Not running is considered success for stopping
        }
    }

    public bool StopOverlay()
    {
        return StopProcess(Overlay);
    }

    public bool StopTrainer()
    {
        return StopProcess(Trainer);
    }

    public void StopAllProcesses()
    {
        foreach (var process in _activeProcesses.Values.ToList())
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping process: {ex.Message}");
            }
        }
        _activeProcesses.Clear();
    }

    public async Task<VrCalibrationStatus> GetStatusAsync()
    {
        await StartProcess(Overlay, []);
        var response = await _httpClient.GetStringAsync($"{_baseUrl}/status");
        return JsonConvert.DeserializeObject<VrCalibrationStatus>(response)!;
    }

    public async Task<bool> StartCamerasAsync(VrCalibration calibration)
    {
        var response = await _httpClient.GetStringAsync(new Uri($"{_baseUrl}/start_cameras?left={calibration.LeftEyeMjpegSource}&right={calibration.RightEyeMjpegSource}"));
        var result = JsonConvert.DeserializeObject<ApiResponse>(response);
        return result!.Result == "ok";
    }

    public async Task<bool> StartCalibrationAsync(VrCalibration calibration)
    {
        var url = $"{_baseUrl}/start_calibration?onnx_filename={Uri.EscapeDataString(calibration.ModelSavePath + VrCalibration.ModelName)}&routine_id={calibration.CalibrationInstructions}";
        var response = await _httpClient.GetStringAsync(url);
        var result = JsonConvert.DeserializeObject<ApiResponse>(response);
        return result!.Result == "ok";
    }

    public async Task<bool> StartPreviewAsync(VrCalibration calibration)
    {
        var modelPath = Uri.EscapeDataString(calibration.ModelSavePath + VrCalibration.ModelName);
        var url = $"{_baseUrl}/start_preview?model_path={Uri.EscapeDataString(modelPath)}";
        var response = await _httpClient.GetStringAsync(url);
        var result = JsonConvert.DeserializeObject<ApiResponse>(response);
        return result!.Result == "ok";
    }

    public async Task<bool> StopPreviewAsync()
    {
        var response = await _httpClient.GetStringAsync($"{_baseUrl}/stop_preview");
        var result = JsonConvert.DeserializeObject<ApiResponse>(response);
        return result!.Result == "ok";
    }

    private void ProcessOutputHandler(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        // Notify subscribers
        ProcessOutputReceived?.Invoke(this, new ProcessOutputEventArgs(e.Data, false));

        // Process against known patterns
        _logger.LogInformation(e.Data);
        ProcessOutput(e.Data);
    }

    private void ProcessErrorHandler(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        // Notify subscribers
        ProcessOutputReceived?.Invoke(this, new ProcessOutputEventArgs(e.Data, true));

        // Process against known patterns (errors might contain important information too)
        _logger.LogError(e.Data);
        ProcessOutput(e.Data);
    }

    private void ProcessOutput(string output)
    {
        foreach (var handler in _responseHandlers)
        {
            if (Regex.IsMatch(output, handler.Key, RegexOptions.IgnoreCase))
            {
                handler.Value.Invoke(output);
                break; // Stop after first match, or remove this line to allow multiple matches
            }
        }
    }

    public void Dispose()
    {
        StopAllProcesses();
    }
}
