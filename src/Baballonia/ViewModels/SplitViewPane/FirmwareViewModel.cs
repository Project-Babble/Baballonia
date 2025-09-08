using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class FirmwareViewModel : ViewModelBase, IDisposable
{
    private readonly FirmwareService _firmwareService = Ioc.Default.GetRequiredService<FirmwareService>();
    private readonly ILogger<FirmwareViewModel> _logger = Ioc.Default.GetRequiredService<ILogger<FirmwareViewModel>>();
    private readonly Dictionary<string, FirmwareSession> _firmwareSessions = new();
    private readonly Dictionary<string, CancellationTokenSource> _animationCancellationTokens = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableSerialPorts = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableWifiNetworks = new();

    [ObservableProperty]
    private string? _selectedSerialPort;

    [ObservableProperty]
    private string? _trackerComboBox = "Click to see trackers...";

    [ObservableProperty]
    private string _wifiSsid;

    [ObservableProperty]
    private string _wifiPassword;

    // [ObservableProperty]
    // private string _mdns = "openiris";

    [ObservableProperty]
    private bool _isDeviceSelectionPresent;

    [ObservableProperty]
    private bool _isValidDeviceSelected;

    [ObservableProperty]
    private bool _isFlashing;

    [ObservableProperty]
    private bool _isFinished;

    [ObservableProperty]
    private string? _modeSetButton = "Set Mode";

    [ObservableProperty]
    private string? _wifiSetButton = "Set Wifi Creds";

    [ObservableProperty]
    private string? _wifiScanButton = "Refresh Wifi Networks";

    [ObservableProperty]
    private string? _selectTracker = "Select Tracker";

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private string? _onRefreshDevicesButton = "Refresh Devices";

    [ObservableProperty] private object? _deviceModeSelectedItem;

    private readonly ProgressBar _progressBar;

    private async Task AnimateEllipsesAsync(string baseText, string propertyName, CancellationToken cancellationToken = default)
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
            // Animation was cancelled, which is expected
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
        if (_animationCancellationTokens.TryGetValue(propertyName, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _animationCancellationTokens.Remove(propertyName);
        }
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
        if (IsDeviceSelectionPresent)
        {
            await Task.Run(async () =>
            {
                // If we haven't already refreshed, create the new firmware session for the
                // Manually typed in tracker
                if (!_firmwareSessions.ContainsKey(SelectedSerialPort!))
                    _firmwareSessions.Add(SelectedSerialPort!, _firmwareService.StartSession(CommandSenderType.Serial, SelectedSerialPort!));

                var res = await TrySendCommandAsync(new FirmwareRequests.SetPausedRequest(true), TimeSpan.FromSeconds(5));
                IsValidDeviceSelected = !string.IsNullOrWhiteSpace(res);
                if (IsValidDeviceSelected)
                {
                    SelectTracker = "Tracker connected!";
                    await Task.Delay(3000);
                    SelectTracker = "Select Tracker";
                }
                else
                {
                    SelectTracker = "The tracker did not respond.";
                    await Task.Delay(3000);
                    SelectTracker = "Select Tracker";
                }
            });
        }
    }

    [RelayCommand]
    private async Task RefreshSerialPorts()
    {
        AvailableSerialPorts.Clear();
        _firmwareSessions.Clear();

        await Task.Run(async () =>
        {
            StartButtonAnimation("Refreshing", nameof(OnRefreshDevicesButton));

            var response = await _firmwareService.ProbeComPortsAsync(TimeSpan.FromSeconds(10));
            TrackerComboBox = $"Found {response.Length} device(s).";
            foreach (var port in response)
            {
                // Only add devices that need a first time set up - IE ones with a heartbeat
                await Dispatcher.UIThread.InvokeAsync(() => AvailableSerialPorts.Add(port));
                _firmwareSessions.Add(port, _firmwareService.StartSession(CommandSenderType.Serial, port));
            }

            StopButtonAnimation(nameof(OnRefreshDevicesButton));
            await Dispatcher.UIThread.InvokeAsync(() => OnRefreshDevicesButton = "Refresh Devices");
        });
    }

    [RelayCommand]
    private async Task RefreshWifiNetworks()
    {
        AvailableWifiNetworks.Clear();

        StartButtonAnimation("Scanning. This will take at most 30 seconds", nameof(WifiScanButton));

        // By this point we should have a valid serial port, no need to do any error wrapping here
        var response = await _firmwareSessions[SelectedSerialPort!].SendCommandAsync(new FirmwareRequests.ScanWifiRequest(), TimeSpan.FromSeconds(30));
        if (response == null)
        {
            StopButtonAnimation(nameof(WifiScanButton));
            WifiScanButton = "Scan failed. Click to try again.";
            return;
        }

        var networks = response!.Networks;
        foreach (var port in networks.
                     OrderByDescending(network => network.Rssi).
                     Select(network => network.Ssid).
                     Where(ssid => !string.IsNullOrEmpty(ssid)))
        {
            AvailableWifiNetworks.Add(port);
        }

        StopButtonAnimation(nameof(WifiScanButton));
        WifiScanButton = $"Found {networks.Count} networks. Click to scan again.";
        HasScanned = true;
    }

    [RelayCommand]
    private async Task SetDeviceMode()
    {
        if (_deviceModeSelectedItem is not ComboBoxItem comboBoxItem)
            return;

        var m = StringToMode(comboBoxItem.Tag!.ToString()!);

        StartButtonAnimation("Setting mode", nameof(ModeSetButton));

        await TrySendCommandAsync(new FirmwareRequests.SetModeRequest(m), TimeSpan.FromSeconds(30));

        StopButtonAnimation(nameof(ModeSetButton));
        ModeSetButton = "Set!";
        await Task.Delay(2000);
        ModeSetButton = "Set Mode";
    }

    [RelayCommand]
    private async Task SendDeviceWifiCredentials()
    {
        StartButtonAnimation("Setting WiFi credentials", nameof(WifiSetButton));

        var res = await TrySendCommandAsync(new FirmwareRequests.SetWifiRequest(WifiSsid, WifiPassword), TimeSpan.FromSeconds(30));

        StopButtonAnimation(nameof(WifiSetButton));
        WifiSetButton = string.IsNullOrEmpty(res) ? "Something went wrong..." : "Sent!";
        await Task.Delay(2000);
        WifiSetButton = "Set Wifi Creds";

        //if (!string.IsNullOrEmpty(Mdns))
        //{
        //    _firmwareSessions[SelectedSerialPort!].SendCommand(new FirmwareRequests.SetMdns(Mdns), TimeSpan.FromSeconds(30));
        //}
    }

    [RelayCommand]
    private async Task FlashFirmware()
    {
        IsFlashing = true;
        await TrySendCommandAsync(new FirmwareRequests.SetPausedRequest(false), TimeSpan.FromSeconds(5));
        _firmwareSessions[SelectedSerialPort!].Dispose();
        await _firmwareService.UploadFirmwareAsync(SelectedSerialPort!, Path.Combine("Firmware", "babble_multimodal_firmware_1.0.0.bin"));
        IsFlashing = false;

        IsFinished = true;
        _firmwareSessions[SelectedSerialPort!] = _firmwareService.StartSession(CommandSenderType.Serial, SelectedSerialPort!);
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

    private async Task<string?> TrySendCommandAsync(IFirmwareRequest request, TimeSpan timeSpan)
    {
        try
        {
            return await _firmwareSessions[SelectedSerialPort!].SendCommandAsync(request, timeSpan);

        }
        catch (Exception e)
        {
            _logger.LogError("Error while sending command {Exception}", e);
            return await Task.FromResult(string.Empty);
        }
    }

    public void Dispose()
    {
        // Stop all button animations
        foreach (var propertyName in _animationCancellationTokens.Keys.ToList())
        {
            StopButtonAnimation(propertyName);
        }

        foreach (var sessions in _firmwareSessions.Values)
        {
            sessions.SendCommand(new FirmwareRequests.SetPausedRequest(false), TimeSpan.FromSeconds(5));
        }
    }
}
