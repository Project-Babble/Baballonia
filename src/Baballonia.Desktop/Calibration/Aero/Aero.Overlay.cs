using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Baballonia.Desktop.Calibration.Aero.Overlay;
using Baballonia.Models;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Baballonia.Desktop.Calibration.Aero;

public partial class AeroOverlayTrainerCombo
{
    private static string Overlay { get; } = null!;
    private static string OverlayPath { get; } = null!;

    static AeroOverlayTrainerCombo()
    {
        if (OperatingSystem.IsWindows())
        {
            OverlayPath = Path.Combine(AppContext.BaseDirectory, "Calibration", "Windows");
            Overlay = Path.Combine(OverlayPath, "gaze_overlay.exe");
        }
        else if (OperatingSystem.IsLinux())
        {
            OverlayPath = Path.Combine(AppContext.BaseDirectory, "Calibration", "Linux");
            Overlay = Path.Combine(OverlayPath, "gaze_overlay");
        }
    }

    private async Task<(bool success, string message)> StartProcess(string program,
        string[]? arguments = null,
        bool waitForExit = false)
    {
        // Make sure the overlay program exists
        if (!File.Exists(program))
        {
            Logger.LogError(Assets.Resources.Aero_Overlay_NotFound);
            return (false, Assets.Resources.Aero_Overlay_NotFound);
        }

        string processName = Path.GetFileNameWithoutExtension(program);

        // Make sure program isn't already running
        var hitList = Process.GetProcesses().Where(p => p.ProcessName == processName).ToArray();
        if (hitList.Length > 0)
        {
            Logger.LogError(Assets.Resources.Aero_Overlay_AlreadyRunning);
            foreach (var p in hitList)
            {
                p.Kill(true);
            }

            // return (false, Assets.Resources.Aero_Overlay_AlreadyRunning);
        }

        // Check if SteamVR is running. The overlay needs it to be running prior!
        // TODO Add Monado here as well
        if (!Process.GetProcesses().Any(p => p.ProcessName.ToLower().Contains("vrserver")))
        {
            Logger.LogError(Assets.Resources.Aero_SteamVR_NotRunning);
            return (false, Assets.Resources.Aero_SteamVR_NotRunning);
        }

        var workingDir = AppContext.BaseDirectory;
        /*if (OperatingSystem.IsWindows())
        {
            if (Directory.Exists("win-x64"))
                workingDir = Path.Combine(AppContext.BaseDirectory, "win-x64");
        }*/

        var startInfo = new ProcessStartInfo
        {
            FileName = program,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDir,
        };

        if (arguments != null)
            startInfo.Arguments = string.Join(" ", arguments);

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Exited += (_, _) =>
        {
            _activeProcesses.Remove(processName);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _activeProcesses[processName] = process;

        if (waitForExit)
        {
            await process.WaitForExitAsync();
        }

        return (true, string.Empty);
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
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Check if it's running but not tracked by us
        var runningProcess = Process.GetProcesses().FirstOrDefault(p => p.ProcessName == processName);
        if (runningProcess != null)
        {
            try
            {
                runningProcess.Kill();
                runningProcess.WaitForExit(3000);
                runningProcess.Dispose();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        return true; // Not running is considered success for stopping
    }

    private void StopAllProcesses()
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
            catch (Exception)
            {
                // ignore
            }
        }
        _activeProcesses.Clear();
    }

    private async Task<VrCalibrationStatus> GetStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync($"{_baseUrl}/status");
            return JsonConvert.DeserializeObject<VrCalibrationStatus>(response)!;
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            throw;
        }
    }

    public async Task<bool> StartPreviewAsync(VrCalibration calibration)
    {
        try
        {
            var modelPath = Uri.EscapeDataString(calibration.ModelSavePath + VrCalibration.ModelName);
            var url = $"{_baseUrl}/start_preview?model_path={Uri.EscapeDataString(modelPath)}";
            var response = await _httpClient.GetStringAsync(url);
            var result = JsonConvert.DeserializeObject<ApiResponse>(response);
            return result!.Result == "ok";
        }
        catch (HttpRequestException e)
        {
            Logger.LogError(e.Message);
            return false;
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            throw;
        }
    }

    public async Task<bool> StopPreviewAsync()
    {
        try
        {
            var response = await _httpClient.GetStringAsync($"{_baseUrl}/stop_preview");
            var result = JsonConvert.DeserializeObject<ApiResponse>(response);
            return result!.Result == "ok";
        }
        catch (HttpRequestException e)
        {
            Logger.LogError(e.Message);
            return false;
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            throw;
        }
    }
}
