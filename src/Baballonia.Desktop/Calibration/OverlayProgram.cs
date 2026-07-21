using Baballonia.Assets;
using Baballonia.Contracts;
using Baballonia.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Baballonia.Desktop.Calibration;

public class OverlayProgram : IOverlayProgram
{
    private readonly ILogger<OverlayProgram> _logger;
    private readonly ILanguageSelectorService _languageSelectorService;
    private readonly string? _executablePath;
    private Process? _process;

    public OverlayProgram(ILogger<OverlayProgram> logger, ILanguageSelectorService languageSelectorService)
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
        _languageSelectorService = languageSelectorService;
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

        var locale = GetGodotLocale();
        // Pass both Godot's engine option and an application option. The latter is applied by
        // MainScene as well, so exported builds cannot silently fall back to English.
        var launchArgs = $"--language {locale} --baballonia-locale={locale}" + (xrMode switch
        {
            XrMode.Debug  => " --use-debug",
            XrMode.OpenVr => " --use-openvr",
            XrMode.OpenXr => " --xr-mode on",
            _ => throw new ArgumentOutOfRangeException()
        });

        _logger.LogInformation("Starting calibration overlay in {Mode} mode with locale {Locale}", xrMode, locale);

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            Arguments = launchArgs,
            UseShellExecute = false
        };

        // The overlay is a separate .NET (Godot Mono) binary with its own runtime and no app-local
        // ICU. Under Steam Runtime 4 (which ships no libicui18n) its globalization init
        // FailFast/SIGABRTs at launch. Run it invariant — it needs no localized cultures, and
        // invariant gives consistent "." decimal formatting for the calibration data it hands back.
        startInfo.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1";

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _process.Start();
    }

    private string GetGodotLocale()
    {
        var language = _languageSelectorService.Language;
        var culture = language == LanguageSelectorService.DefaultLanguage
            ? CultureInfo.CurrentUICulture
            : CultureInfo.GetCultureInfo(language);

        return culture.Name switch
        {
            "zh-CN" => "zh_CN",
            "zh-TW" => "zh_TW",
            _ => culture.TwoLetterISOLanguageName
        };
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
