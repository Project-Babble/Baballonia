using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Baballonia.Assets;
using Microsoft.Extensions.Logging;

namespace Baballonia.Desktop.Calibration.Aero;

public class OverlayProgram : IOverlayProgram, IDisposable
{
    private ILogger<OverlayProgram> _logger;
    private string? _executablePath;
    private Process? _process;

    public OverlayProgram(ILogger<OverlayProgram> logger)
    {
        var isWindows = OperatingSystem.IsWindows();
        var isArm = RuntimeInformation.OSArchitecture is Architecture.Arm or Architecture.Arm64 or Architecture.Armv6;
        var architectureIdentifier = isArm ? "arm64" : "x86_64";
        var OverlayPath = Path.Combine(AppContext.BaseDirectory, "Calibration", isWindows ? "Windows" : "Linux",
            "Overlay");
        var Overlay = Path.Combine(OverlayPath,
            isWindows ? $"BabbleCalibration.{architectureIdentifier}.exe" : $"BabbleCalibration.{architectureIdentifier}");
        _executablePath = Overlay;
        _logger = logger;
    }

    public bool CanStart()
    {
        if (!File.Exists(_executablePath))
        {
            _logger.LogError("Trainer program not found: {} not exists", _executablePath);
            return false;
        }
        return true;
    }

    public void Start()
    {
        if (_executablePath == null)
            return;

        _process?.Kill();

        var hitList = Process.GetProcesses()
            .Where(p => p.ProcessName == Path.GetFileNameWithoutExtension(_executablePath)).ToArray();
        if (hitList.Length > 0)
        {
            foreach (var p in hitList) p.Kill(true);
        }


        var processList = Process.GetProcesses();
        var steamvr = processList.Any(p => p.ProcessName.ToLower().Contains("vrserver"));
        var monado = processList.Any(p => p.ProcessName.ToLower().Contains("monado"));
        var isWindows = OperatingSystem.IsWindows();

        var launchArgs = $"-l {Resources.Godot_Locale}";

        if (!steamvr && !monado)
        {
            launchArgs += " --use-debug";
        }
        else
        {
            if (isWindows)
            {
                if (steamvr)
                    launchArgs += " --use-openvr";
                else if (monado) launchArgs += " --xr-mode on"; //uhhhhh?????
            }
            else
            {
                launchArgs += " --xr-mode on";
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            Arguments = launchArgs
        };

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _process.Start();
    }

    public Task WaitForExitAsync()
    {
        if (_process == null)
            return Task.CompletedTask;

        return _process.WaitForExitAsync();
    }

    public void Dispose()
    {
        _process?.Kill();
        _process = null;
    }
}
