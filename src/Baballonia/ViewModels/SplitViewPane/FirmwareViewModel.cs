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
using Baballonia.Assets;
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
    private ObservableCollection<string> _availableFirmwareTypes = new();

    [ObservableProperty]
    private int _selectedFirmwareIndex;

    private readonly string _bundledFirmwarePath = Path.Combine(
        AppContext.BaseDirectory,
        "Firmware",
        "Binaries");

    public string CustomFirmwarePath;

    [ObservableProperty]
    private string? _selectedSerialPort;

    [ObservableProperty]
    private string? _trackerComboBox = Resources.Firmware_TrackerComboBox_Default;

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
    private string? _modeSetButton = Resources.Firmware_ModeSetButton_Default;

    [ObservableProperty]
    private string? _wifiSetButton = Resources.Firmware_WifiSetButton_Default;

    [ObservableProperty]
    private string? _wifiScanButton = Resources.Firmware_WifiScanButton_Default;

    [ObservableProperty]
    private string? _selectTracker = Resources.Firmware_SelectTracker_Default;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private string? _onRefreshDevicesButton = Resources.Firmware_RefreshDevices_Default;

    [ObservableProperty] private object? _deviceModeSelectedItem;

    public FirmwareViewModel()
    {
        AvailableFirmwareTypes.Clear();
        var binaries = Directory.
            GetFiles(_bundledFirmwarePath, "*.bin").
            OrderByDescending(x => x.Length);
        foreach (var bin in binaries)
        {
            AvailableFirmwareTypes.Add(Path.GetFileName(bin));
        }
    }

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
                    SelectTracker = Resources.Firmware_SelectTracker_Connected;
                    await Task.Delay(3000);
                    SelectTracker = Resources.Firmware_SelectTracker_Default;
                }
                else
                {
                    SelectTracker = Resources.Firmware_SelectTracker_NoResponse;
                    await Task.Delay(3000);
                    SelectTracker = Resources.Firmware_SelectTracker_Default;
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
            StartButtonAnimation(Resources.Firmware_RefreshDevices_Refreshing, nameof(OnRefreshDevicesButton));

            var response = await _firmwareService.ProbeComPortsAsync(TimeSpan.FromSeconds(10));
            TrackerComboBox = string.Format(Resources.Firmware_RefreshDevices_Found, response.Length);
            foreach (var port in response)
            {
                // Only add devices that need a first time set up - IE ones with a heartbeat
                await Dispatcher.UIThread.InvokeAsync(() => AvailableSerialPorts.Add(port));
                _firmwareSessions.Add(port, _firmwareService.StartSession(CommandSenderType.Serial, port));
            }

            StopButtonAnimation(nameof(OnRefreshDevicesButton));
            await Dispatcher.UIThread.InvokeAsync(() => OnRefreshDevicesButton = Resources.Firmware_RefreshDevices_Default);
        });
    }

    [RelayCommand]
    private async Task RefreshWifiNetworks()
    {
        AvailableWifiNetworks.Clear();

        StartButtonAnimation(Resources.Firmware_WifiScanButton_Scanning, nameof(WifiScanButton));

        // By this point we should have a valid serial port, no need to do any error wrapping here
        var response = await _firmwareSessions[SelectedSerialPort!].SendCommandAsync(new FirmwareRequests.ScanWifiRequest(), TimeSpan.FromSeconds(30));
        if (response == null)
        {
            StopButtonAnimation(nameof(WifiScanButton));
            WifiScanButton = Resources.Firmware_WifiScanButton_Error;
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

        await TrySendCommandAsync(new FirmwareRequests.SetModeRequest(m), TimeSpan.FromSeconds(30));

        StopButtonAnimation(nameof(ModeSetButton));
        ModeSetButton = Resources.Firmware_ModeSetButton_Success;
        await Task.Delay(2000);
        ModeSetButton = Resources.Firmware_ModeSetButton_Default;
    }

    [RelayCommand]
    private async Task SendDeviceWifiCredentials()
    {
        StartButtonAnimation(Resources.Firmware_WifiSetButton_Setting, nameof(WifiSetButton));

        var res = await TrySendCommandAsync(new FirmwareRequests.SetWifiRequest(WifiSsid, WifiPassword), TimeSpan.FromSeconds(30));

        StopButtonAnimation(nameof(WifiSetButton));
        WifiSetButton = string.IsNullOrEmpty(res) ? Resources.Firmware_WifiSetButton_Error : Resources.Firmware_WifiSetButton_Success;
        await Task.Delay(2000);
        WifiSetButton = Resources.Firmware_WifiSetButton_Default;

        //if (!string.IsNullOrEmpty(Mdns))
        //{
        //    _firmwareSessions[SelectedSerialPort!].SendCommand(new FirmwareRequests.SetMdns(Mdns), TimeSpan.FromSeconds(30));
        //}
    }

    [RelayCommand]
    private async Task FlashFirmware()
    {
        if (_firmwareSessions.TryGetValue(SelectedSerialPort!, out FirmwareSession? value))
        {
            // True, this is a multimodal device that needs to be released prior to flashing
            await TrySendCommandAsync(new FirmwareRequests.SetPausedRequest(false), TimeSpan.FromSeconds(5));
            value.Dispose();
        }
        else if (!_firmwareService.FindAvailableSerialPorts().Contains(SelectedSerialPort))
        {
            // If we don't have a multimodal device, this is most likely a legacy device we're upgrading. No need to release!
            // However, we need to make sure the user's input is an actual valid serial port
            return;
        }

        // Check if the user has selected custom firmware for upload
        var candidateFirmwarePath = Path.Combine(_bundledFirmwarePath, AvailableFirmwareTypes[SelectedFirmwareIndex]);
        if (File.Exists(candidateFirmwarePath))
        {
            // Combobox selection
            IsFlashing = true;
            await _firmwareService.UploadFirmwareAsync(SelectedSerialPort!, candidateFirmwarePath);
        }
        else if (!string.IsNullOrEmpty(CustomFirmwarePath))
        {
            // Else, pass in the absolute path
            IsFlashing = true;
            await _firmwareService.UploadFirmwareAsync(SelectedSerialPort!, CustomFirmwarePath);
        }
        else
        {
            return;
        }
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
