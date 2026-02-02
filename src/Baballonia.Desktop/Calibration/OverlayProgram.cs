using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Baballonia.Assets;
using Microsoft.Extensions.Logging;

namespace Baballonia.Desktop.Calibration;

public class OverlayProgram : IOverlayProgram
{
    private readonly ILogger<OverlayProgram> _logger;
    private readonly string? _executablePath;
    private Process? _process;

    public OverlayProgram(ILogger<OverlayProgram> logger)
    {
        var isWindows = OperatingSystem.IsWindows();
        var isArm = RuntimeInformation.OSArchitecture is Architecture.Arm or Architecture.Arm64 or Architecture.Armv6;
        var architectureIdentifier = isArm ? "arm64" : "x86_64";
        var overlayPath = Path.Combine(AppContext.BaseDirectory, "Calibration", isWindows ? "Windows" : "Linux",
            "Overlay");
        var overlay = Path.Combine(overlayPath,
            isWindows ? $"BabbleCalibration.{architectureIdentifier}.exe" : $"BabbleCalibration.{architectureIdentifier}");
        _executablePath = overlay;
        _logger = logger;
    }

    public bool CanStart()
    {
        if (File.Exists(_executablePath)) return true;
        _logger.LogError("Trainer program not found: {} not exists", _executablePath);
        return false;
    }

    public void Start()
    {
        _process?.Kill();

        var processName = Path.GetFileNameWithoutExtension(_executablePath);
        foreach (var p in Process.GetProcesses().Where(p => p.ProcessName == processName))
        {
            p.Kill(true);
        }

        var processes = Process.GetProcesses();
        var hasSteamVr = IsProcessRunning(processes, "vrserver");
        var hasMonado = IsProcessRunning(processes, "monado");
        var hasWivrn = IsProcessRunning(processes, "wivrn-server");

        var xrMode =
            !hasSteamVr && !hasMonado && !hasWivrn ? XrMode.Debug :
            OperatingSystem.IsWindows() && hasSteamVr ? XrMode.OpenVr :
            XrMode.OpenXr;

        var launchArgs = $"-l {Resources.Godot_Locale}" + xrMode switch
        {
            XrMode.Debug  => " --use-debug",
            XrMode.OpenVr => " --use-openvr",
            XrMode.OpenXr => " --xr-mode on",
            _ => throw new ArgumentOutOfRangeException()
        };

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

    private static bool IsProcessRunning(Process[] ps, string name) =>
        ps.Any(p => p.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase));

    public Task WaitForExitAsync()
    {
        return _process == null ?
            Task.CompletedTask :
            _process.WaitForExitAsync();
    }

    public void Dispose()
    {
        _process?.Kill();
        _process = null;
    }

    private enum XrMode
    {
        Debug,
        OpenVr,
        OpenXr
    }

}
