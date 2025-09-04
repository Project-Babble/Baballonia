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

namespace Baballonia.ViewModels.SplitViewPane;

public partial class FirmwareViewModel : ViewModelBase, IDisposable
{
    private readonly FirmwareService _firmwareService;
    private readonly Dictionary<string, FirmwareSession> _firmwareSessions = new();

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
    private bool _isValidDeviceSelected;

    [ObservableProperty]
    private string? _modeSetButton = "Set Mode";

    [ObservableProperty]
    private string? _wifiSetButton = "Set Wifi Creds";

    [ObservableProperty]
    private string? _wifiScanButton = "Refresh Wifi Networks";

    [ObservableProperty]
    private string? _onRefreshDevicesButton = "Refresh Devices";

    [ObservableProperty] private object? _deviceModeSelectedItem;

    public FirmwareViewModel()
    {
        _firmwareService = Ioc.Default.GetRequiredService<FirmwareService>();
    }

    partial void OnSelectedSerialPortChanged(string? oldValue, string? newValue)
    {
        IsValidDeviceSelected = !string.IsNullOrEmpty(newValue);
        if (!IsValidDeviceSelected) return;
        SelectedSerialPort = newValue;
        Task.Run(async () =>
        {
            await _firmwareSessions[SelectedSerialPort!]
                .SendCommandAsync(new FirmwareRequests.SetPausedRequest(true), TimeSpan.FromSeconds(5));
        });
    }

    [RelayCommand]
    private async Task RefreshSerialPorts()
    {
        AvailableSerialPorts.Clear();
        _firmwareSessions.Clear();

        await Task.Run(async () =>
        {
            OnRefreshDevicesButton = "Refreshing...";
            var response = await _firmwareService.ProbeComPortsAsync(TimeSpan.FromSeconds(10));
            TrackerComboBox = $"Found {response.Length} device(s).";
            foreach (var port in response)
            {
                // Only add devices that need a first time set up - IE ones with a heartbeat
                AvailableSerialPorts.Add(port);
                _firmwareSessions.Add(port, _firmwareService.StartSession(CommandSenderType.Serial, port));
            }
            OnRefreshDevicesButton = "Refresh Devices";
        });
    }

    [RelayCommand]
    private async Task RefreshWifiNetworks()
    {
        AvailableWifiNetworks.Clear();

        WifiScanButton = "Scanning. This might take a while...";
        var response = await _firmwareSessions[SelectedSerialPort!].SendCommandAsync(new FirmwareRequests.ScanWifiRequest(), TimeSpan.FromSeconds(30));
        if (response == null) return;

        var networks = response!.Networks;
        foreach (var port in networks.
                     OrderByDescending(network => network.Rssi).
                     Select(network => network.Ssid).
                     Where(ssid => !string.IsNullOrEmpty(ssid)))
        {
            AvailableWifiNetworks.Add(port);
        }

        WifiScanButton = $"Found {networks.Count} networks. Click to scan again.";
    }


    [RelayCommand]
    private async Task SetDeviceMode()
    {
        if (_deviceModeSelectedItem is not ComboBoxItem comboBoxItem)
            return;

        var m = StringToMode(comboBoxItem.Tag!.ToString()!);
        ModeSetButton = "Setting mode...";
        await _firmwareSessions[SelectedSerialPort!].SendCommandAsync(new FirmwareRequests.SetModeRequest(m), TimeSpan.FromSeconds(30));

        ModeSetButton = "Set!";
        await Task.Delay(2000);
        ModeSetButton = "Set Mode";
    }

    [RelayCommand]
    private async Task SendDeviceWifiCredentials()
    {
        var res = await _firmwareSessions[SelectedSerialPort!].SendCommandAsync(new FirmwareRequests.SetWifiRequest(WifiSsid, WifiPassword), TimeSpan.FromSeconds(30));
        WifiSetButton = string.IsNullOrEmpty(res) ? "Something went wrong..." : "Sent!";
        await Task.Delay(2000);
        WifiSetButton = "Set Wifi Creds";

        //if (!string.IsNullOrEmpty(Mdns))
        //{
        //    _firmwareSessions[SelectedSerialPort!].SendCommand(new FirmwareRequests.SetMdns(Mdns), TimeSpan.FromSeconds(30));
        //}
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

    public void Dispose()
    {
        foreach (var sessions in _firmwareSessions.Values)
        {
            sessions.SendCommand(new FirmwareRequests.SetPausedRequest(false), TimeSpan.FromSeconds(5));
        }
    }
}
