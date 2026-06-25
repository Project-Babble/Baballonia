using Avalonia.Controls;
using Avalonia.Threading;
using Baballonia.Assets;
using Baballonia.Contracts;
using Baballonia.Models;
using Baballonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Baballonia.ViewModels.SplitViewPane;

// TODO: New firmware now exposes the restart command. Possible automation to restart the board automatically after mode switch.
// caveat: it changes the com port
// possible solution: before restarting query and store the serial number and rescan the boards automatically assigning the one with the same serial as selected

public partial class FirmwareViewModel : ViewModelBase, IDisposable
{
    private readonly FirmwareService _firmwareService = Ioc.Default.GetRequiredService<FirmwareService>();
    private readonly ILogger<FirmwareViewModel> _logger = Ioc.Default.GetRequiredService<ILogger<FirmwareViewModel>>();
    private readonly Dictionary<string, IFirmwareSession> _firmwareSessions = new();
    private readonly Dictionary<string, CancellationTokenSource> _animationCancellationTokens = new();
    private readonly FirmwareSessionFactory _firmwareSessionFactory;

    // Pipeline managers (singletons) own the running camera feeds; used to release ONLY serial
    // camera handles on Refresh so the firmware probe can open the port.
    private readonly Baballonia.Services.Inference.FacePipelineManager _facePipeline = Ioc.Default.GetRequiredService<Baballonia.Services.Inference.FacePipelineManager>();
    private readonly EyePipelineManager _eyePipeline = Ioc.Default.GetRequiredService<EyePipelineManager>();

    // Guards RefreshSerialPorts against re-entrancy (RelayCommand does not serialize invocations,
    // and the body disposes/mutates _firmwareSessions).
    private bool _isRefreshing;

    // Set in Dispose(); lets an in-flight flash that finishes after the tab is left skip
    // recreating a live session that would never be released.
    private bool _disposed;

    [ObservableProperty] private ObservableCollection<string> _availableSerialPorts = [];

    [ObservableProperty] private ObservableCollection<string> _availableWifiNetworks = [];

    [ObservableProperty] private ObservableCollection<string> _availableFirmwareTypes = [];

    [ObservableProperty] private int _selectedFirmwareIndex;

    private readonly string _bundledFirmwarePath = Path.Combine(
        AppContext.BaseDirectory,
        "Firmware",
        "Binaries");

    public string CustomFirmwarePath;

    [ObservableProperty] private string? _selectedSerialPort;

    [ObservableProperty] private string? _trackerComboBox = Resources.Firmware_TrackerComboBox_Default;

    [ObservableProperty] private string _wifiSsid;

    [ObservableProperty] private string _wifiPassword;

    // [ObservableProperty]
    // private string _mdns = "openiris";

    [ObservableProperty] private bool _isDeviceSelectionPresent;

    [ObservableProperty] private bool _isValidDeviceSelected;

    [ObservableProperty] private bool _isFlashing;

    [ObservableProperty] private bool _isFinished;

    // True when the last flash attempt failed; drives the red failure message in the view.
    [ObservableProperty] private bool _isFlashFailed;

    // Latest line of espflash output, shown live under the flashing progress bar.
    [ObservableProperty] private string? _flashStatus;

    [ObservableProperty] private string? _modeSetButton = Resources.Firmware_ModeSetButton_Default;

    [ObservableProperty] private string? _wifiSetButton = Resources.Firmware_WifiSetButton_Default;

    [ObservableProperty] private string? _wifiScanButton = Resources.Firmware_WifiScanButton_Default;

    [ObservableProperty] private string? _selectTracker = Resources.Firmware_SelectTracker_Default;

    [ObservableProperty] private bool _hasScanned;

    [ObservableProperty] private string? _onRefreshDevicesButton = Resources.Firmware_RefreshDevices_Default;

    [ObservableProperty] private object? _deviceModeSelectedItem;

    public FirmwareViewModel(FirmwareSessionFactory firmwareSessionFactory)
    {
        _firmwareSessionFactory = firmwareSessionFactory;
        AvailableFirmwareTypes.Clear();
        var binaries = Directory.
            GetFiles(_bundledFirmwarePath, "*.bin").
            OrderByDescending(x => x); // descending version number
        foreach (var bin in binaries)
        {
            AvailableFirmwareTypes.Add(Path.GetFileName(bin));
        }

        _firmwareService.OnFirmwareUpdateProgress += OnFlashProgress;
    }

    // espflash output arrives on a background thread; marshal the status to the UI thread.
    private void OnFlashProgress(string status) =>
        Dispatcher.UIThread.Post(() => FlashStatus = status);

    private async Task AnimateEllipsesAsync(string baseText, string propertyName,
        CancellationToken cancellationToken = default)
    {
        var ellipsesStates = new[] { ".", "..", "..." };
        var currentIndex = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var animatedText = $"{baseText}{ellipsesStates[currentIndex]}";

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    switch (propertyName)
                    {
                        case nameof(OnRefreshDevicesButton):
                            OnRefreshDevicesButton = animatedText;
                            break;
                        case nameof(WifiScanButton):
                            WifiScanButton = animatedText;
                            break;
                        case nameof(ModeSetButton):
                            ModeSetButton = animatedText;
                            break;
                        case nameof(WifiSetButton):
                            WifiSetButton = animatedText;
                            break;
                    }
                });

                currentIndex = (currentIndex + 1) % ellipsesStates.Length;
                await Task.Delay(500, cancellationToken); // Update every 500ms
            }
        }
        catch (OperationCanceledException)
        {
            // Animation was canceled, which is expected
        }
    }

    private void StartButtonAnimation(string baseText, string propertyName)
    {
        StopButtonAnimation(propertyName);

        var cts = new CancellationTokenSource();
        _animationCancellationTokens[propertyName] = cts;

        _ = Task.Run(async () => await AnimateEllipsesAsync(baseText, propertyName, cts.Token));
    }

    private void StopButtonAnimation(string propertyName)
    {
        if (!_animationCancellationTokens.TryGetValue(propertyName, out var cts)) return;
        cts.Cancel();
        cts.Dispose();
        _animationCancellationTokens.Remove(propertyName);
    }

    partial void OnSelectedSerialPortChanged(string? oldValue, string? newValue)
    {
        IsDeviceSelectionPresent = !string.IsNullOrEmpty(newValue);
        if (IsDeviceSelectionPresent)
        {
            SelectedSerialPort = newValue;
        }
        else
        {
            IsValidDeviceSelected = false;
        }
    }

    [RelayCommand]
    private async Task SelectSerialPort()
    {
        // No, 'openiristracker' is not a valid COM object or device path
        if (IsDeviceSelectionPresent && CanConnect(SelectedSerialPort!))
        {
            // If we haven't already refreshed, create the new firmware session for the
            // Manually typed in tracker
            if (!_firmwareSessions.ContainsKey(SelectedSerialPort!))
            {
                var s = await _firmwareSessionFactory.TryOpenSessionAsync(SelectedSerialPort!);
                // TODO:
                // ??????? what to do if we cant open session????
                _firmwareSessions.Add(SelectedSerialPort!, s);
            }

            if (_firmwareSessions.TryGetValue(SelectedSerialPort!, out var session))
            {
                if (session == null)
                {
                    IsValidDeviceSelected = false;
                }
                else if (session.Version < new Version(0, 0, 1))      // open v1 device. v1 devices do not report version, but they respond to commands
                {
                    var res = await TrySendCommandAsync(new FirmwareRequests.SetPausedRequest(true),
                        TimeSpan.FromSeconds(5));
                    IsValidDeviceSelected = res.IsSuccess;
                }
                else if (session.Version >= new Version(0, 2, 0)) // open v2 device. v2 devices report version and respond to commands
                {
                    IsValidDeviceSelected = true;
                }
                else
                {
                    IsValidDeviceSelected = false;               // wtf?
                }
            }
            else
            {
                IsValidDeviceSelected = false;                    // open legacy device. legacy devices do not have versions or commands
            }

            SelectTracker = IsValidDeviceSelected ?
                Resources.Firmware_SelectTracker_Connected :
                Resources.Firmware_SelectTracker_NoResponse;

            await Task.Delay(3000);
            SelectTracker = Resources.Firmware_SelectTracker_Default;
        }
    }

    // Plucked from SerialCameraCaptureFactory.cs
    private bool CanConnect(string address)
    {
        var lowered = address.ToLower();
        return lowered.StartsWith("com") ||
               lowered.StartsWith("/dev/tty") ||
               lowered.StartsWith("/dev/cu") ||
               lowered.StartsWith("/dev/ttyacm");
    }

    [RelayCommand]
    private async Task RefreshSerialPorts()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var previousSelection = SelectedSerialPort;

            // Serial cameras hold the COM/tty port exclusively; release ONLY those so the probe
            // below can open them. UVC (/dev/videoN) and IP feeds keep tracking. This is the ONLY
            // place that stops serial video — merely navigating to the firmware tab never does.
            _facePipeline.StopSerialCameras();
            _eyePipeline.StopSerialCameras();

            AvailableSerialPorts.Clear();
            // Dispose stale sessions before re-probing; a dead/re-plugged port can throw on close.
            foreach (var s in _firmwareSessions.Values)
            {
                try { s?.Dispose(); }
                catch (Exception e) { _logger.LogDebug("Error disposing stale session: {Exception}", e); }
            }
            _firmwareSessions.Clear();

            StartButtonAnimation(Resources.Firmware_RefreshDevices_Refreshing, nameof(OnRefreshDevicesButton));

            // The probe runs async (one Task.Run per port inside the factory), so the UI thread
            // stays responsive while we await it. Crucially every _firmwareSessions / bound-property
            // mutation below stays on the UI thread, avoiding a cross-thread Dictionary race with
            // Dispose() (which runs on the UI thread during tab navigation).
            var candidates = await _firmwareSessionFactory.TryOpenAllSessionsAsync();
            TrackerComboBox = string.Format(Resources.Firmware_RefreshDevices_Found, candidates.Count());

            foreach (var mappings in candidates)
            {
                AvailableSerialPorts.Add(mappings.Port);
                _firmwareSessions.Add(mappings.Port, mappings.Session);
            }

            StopButtonAnimation(nameof(OnRefreshDevicesButton));
            OnRefreshDevicesButton = Resources.Firmware_RefreshDevices_Default;

            // Re-validate the selection: if the previously selected port vanished (e.g. the device
            // was re-plugged and re-enumerated under a new name) clear it so the user can't act on
            // a dead port. A still-present selection is preserved.
            if (!string.IsNullOrEmpty(previousSelection) && AvailableSerialPorts.Contains(previousSelection))
                SelectedSerialPort = previousSelection;
            else
                SelectedSerialPort = null;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task RefreshWifiNetworks()
    {
        AvailableWifiNetworks.Clear();

        StartButtonAnimation(Resources.Firmware_WifiScanButton_Scanning, nameof(WifiScanButton));

        // Session may have been invalidated (e.g. port closed / re-plugged); guard the lookup.
        if (string.IsNullOrEmpty(SelectedSerialPort) ||
            !_firmwareSessions.TryGetValue(SelectedSerialPort, out var wifiSession) || wifiSession is null)
        {
            StopButtonAnimation(nameof(WifiScanButton));
            WifiScanButton = Resources.Firmware_WifiScanButton_Error;
            return;
        }

        var response = await wifiSession
            .SendCommandAsync(new FirmwareRequests.ScanWifiRequest(), TimeSpan.FromSeconds(30));
        if (!response.IsSuccess)
        {
            StopButtonAnimation(nameof(WifiScanButton));
            WifiScanButton = Resources.Firmware_WifiScanButton_Error;
            return;
        }

        var networks = response.Value!.Networks;
        foreach (var port in networks.OrderByDescending(network => network.Rssi).Select(network => network.Ssid)
                     .Where(ssid => !string.IsNullOrEmpty(ssid)))
        {
            AvailableWifiNetworks.Add(port);
        }

        StopButtonAnimation(nameof(WifiScanButton));
        WifiScanButton = string.Format(Resources.Firmware_WifiScanButton_Success, networks.Count);
        HasScanned = true;
    }

    [RelayCommand]
    private async Task SetDeviceMode()
    {
        if (_deviceModeSelectedItem is not ComboBoxItem comboBoxItem)
            return;

        var m = StringToMode(comboBoxItem.Tag!.ToString()!);

        StartButtonAnimation(Resources.Firmware_ModeSetButton_Setting, nameof(ModeSetButton));

        // TODO: add error handling in case command fails for whatever reason
        await TrySendCommandAsync(new FirmwareRequests.SetModeRequest(m), TimeSpan.FromSeconds(5));

        StopButtonAnimation(nameof(ModeSetButton));
        ModeSetButton = Resources.Firmware_ModeSetButton_Success;
        await Task.Delay(2000);
        ModeSetButton = Resources.Firmware_ModeSetButton_Default;
    }

    [RelayCommand]
    private async Task SendDeviceWifiCredentials()
    {
        StartButtonAnimation(Resources.Firmware_WifiSetButton_Setting, nameof(WifiSetButton));

        var res = await TrySendCommandAsync(new FirmwareRequests.SetWifiRequest(WifiSsid, WifiPassword),
            TimeSpan.FromSeconds(30));

        StopButtonAnimation(nameof(WifiSetButton));
        WifiSetButton = !res.IsSuccess
            ? Resources.Firmware_WifiSetButton_Error
            : Resources.Firmware_WifiSetButton_Success;
        await Task.Delay(2000);
        WifiSetButton = Resources.Firmware_WifiSetButton_Default;

        // By this point we should have a valid serial port, no need to do any error wrapping here
        //if (!string.IsNullOrEmpty(Mdns))
        //{
        //    _firmwareSessions[SelectedSerialPort!].SendCommand(new FirmwareRequests.SetMdns(Mdns), TimeSpan.FromSeconds(30));
        //}
    }

    [RelayCommand]
    private async Task FlashFirmware()
    {
        FlashStatus = null; // clear any status left over from a previous flash

        IsFlashFailed = false;
        IsFinished = false; // clear any lingering "Done!" so success and failure can't both show

        if (_firmwareSessions.TryGetValue(SelectedSerialPort!, out var value))
        {
            if (value == null) return;

            // Multimodal device: release the stream before flashing (no-op on firmware that doesn't
            // support pause). Then dispose the session so espflash can take the serial port, and
            // drop it from the map so a disposed session is never reused.
            await TrySendCommandAsync(new FirmwareRequests.SetPausedRequest(false), TimeSpan.FromSeconds(5));
            value.Dispose();
            _firmwareSessions.Remove(SelectedSerialPort!);
        }
        else if (!_firmwareService.FindAvailableSerialPorts().Contains(SelectedSerialPort))
        {
            // If we don't have a multimodal device, this is most likely a legacy device we're upgrading. No need to release!
            // However, we need to make sure the user's input is an actual valid serial port
            return;
        }

        // Check if the user has selected custom firmware for upload
        var candidateFirmwarePath = Path.Combine(_bundledFirmwarePath, AvailableFirmwareTypes[SelectedFirmwareIndex]);
        bool success;
        if (File.Exists(candidateFirmwarePath))
        {
            // Combobox selection
            IsFlashing = true;
            success = await _firmwareService.UploadFirmwareAsync(SelectedSerialPort!, candidateFirmwarePath);
        }
        else if (!string.IsNullOrEmpty(CustomFirmwarePath))
        {
            // Else, pass in the absolute path
            IsFlashing = true;
            success = await _firmwareService.UploadFirmwareAsync(SelectedSerialPort!, CustomFirmwarePath);
        }
        else
        {
            return;
        }

        IsFlashing = false;

        if (!success)
        {
            // espflash failed (e.g. "Error while connecting to device"). Show a clear failure
            // instead of "Done!", and do NOT recreate a session against a device that didn't flash.
            if (string.IsNullOrWhiteSpace(FlashStatus))
                FlashStatus = Resources.Firmware_Flashing_Failed_Detail;
            IsFlashFailed = true;
            IsValidDeviceSelected = false; // the session was disposed above; require a re-refresh
            await Task.Delay(8000);
            IsFlashFailed = false;
            return;
        }

        // If the user navigated away mid-flash the VM was disposed; don't create a new live session
        // (it would never be released and would keep holding the port).
        if (_disposed) return;

        IsFinished = true;
        // No need to check if this is a valid Babble tracker - treat it like a normal device
        _firmwareSessions[SelectedSerialPort!] =
            _firmwareService.StartSession(CommandSenderType.Serial, SelectedSerialPort!);
        await Task.Delay(5000);
        IsFinished = false;
    }

    private static FirmwareRequests.Mode StringToMode(string mode)
    {
        return mode switch
        {
            "auto" => FirmwareRequests.Mode.Auto,
            "wifi" => FirmwareRequests.Mode.Wifi,
            "uvc" => FirmwareRequests.Mode.UVC,
            _ => FirmwareRequests.Mode.Auto
        };
    }

    private async Task<FirmwareResponse<JsonDocument>> TrySendCommandAsync(IFirmwareRequest request, TimeSpan timeSpan)
    {
        var port = SelectedSerialPort;
        if (string.IsNullOrEmpty(port) ||
            !_firmwareSessions.TryGetValue(port, out var session) || session is null)
            return FirmwareResponse<JsonDocument>.Failure("No active session.");

        // Skip requests the firmware version doesn't support (e.g. SetPausedRequest on v2 firmware)
        // so they become a benign no-op instead of throwing NotSupportedException.
        if (!RequestVersionGuard.IsSupported(request, session.Version))
        {
            _logger.LogDebug("Skipping {Request}; not supported by firmware v{Version}",
                request.GetType().Name, session.Version);
            return FirmwareResponse<JsonDocument>.Failure(
                $"{request.GetType().Name} not supported by firmware v{session.Version}.");
        }

        try
        {
            return await session.SendCommandAsync(request, timeSpan);
        }
        catch (Exception e) when (e is ObjectDisposedException or IOException or InvalidOperationException)
        {
            // Port closed / device re-plugged: drop the dead session so it isn't reused.
            _logger.LogWarning("Port {Port} appears closed; invalidating session. {Message}", port, e.Message);
            InvalidateSession(port);
            return FirmwareResponse<JsonDocument>.Failure("Device port is closed.");
        }
        catch (Exception e)
        {
            _logger.LogError("Error while sending command {Exception}", e);
            return FirmwareResponse<JsonDocument>.Failure("Error while sending command.");
        }
    }

    // Disposes and removes a session whose port has gone away, reflecting it in the UI so the user
    // isn't left able to act on a dead device.
    private void InvalidateSession(string? port)
    {
        if (string.IsNullOrEmpty(port)) return;
        if (_firmwareSessions.TryGetValue(port, out var s))
        {
            try { s?.Dispose(); } catch { /* port already dead */ }
            _firmwareSessions.Remove(port);
        }
        if (port == SelectedSerialPort)
            IsValidDeviceSelected = false;
    }

    public void Dispose()
    {
        _disposed = true;
        _firmwareService.OnFirmwareUpdateProgress -= OnFlashProgress;

        // Stop all button animations
        foreach (var propertyName in _animationCancellationTokens.Keys.ToList())
        {
            StopButtonAnimation(propertyName);
        }

        // Release every serial connection on leaving the tab. Snapshot + clear synchronously so the
        // dictionary is empty immediately and can't race anything; run the (possibly slow) unpause +
        // dispose on a background thread so switching tabs never freezes the UI for up to 5s.
        var toRelease = _firmwareSessions.Values.Where(s => s is not null).ToList();
        _firmwareSessions.Clear();
        if (toRelease.Count == 0)
            return;

        Task.Run(() =>
        {
            foreach (var session in toRelease)
            {
                try
                {
                    // Best-effort unpause only for firmware that supports it (V1, v0.0.0).
                    var unpause = new FirmwareRequests.SetPausedRequest(false);
                    if (RequestVersionGuard.IsSupported(unpause, session.Version))
                        session.SendCommand(unpause, TimeSpan.FromSeconds(5));
                }
                catch (Exception e) when (e is NotSupportedException or ObjectDisposedException or IOException or InvalidOperationException)
                {
                    _logger.LogDebug("Dispose: skipping unpause: {Message}", e.Message);
                }
                finally
                {
                    try { session.Dispose(); } catch { /* already gone */ }
                }
            }
        });
    }
}
