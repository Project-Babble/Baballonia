using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class FirmwareViewModel : ViewModelBase
{
    #region Services and Dependencies

    private readonly HomePageView _homePageView;
    private readonly GithubService _githubService;
    private readonly FirmwareService _firmwareService;
    private readonly ILocalSettingsService _settingsService;

    #endregion

    #region Properties

    [ObservableProperty]
    private bool _isDeviceSelected;

    [ObservableProperty]
    private bool _isWirelessFirmware;

    [ObservableProperty]
    private bool _wirelessWarningVisible;

    [ObservableProperty]
    private bool _isFlashing;

    [ObservableProperty]
    private bool _isFinished;

    [ObservableProperty]
    private string _wifiSsid = string.Empty;

    [ObservableProperty]
    private string _wifiPassword = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _availableFirmwareTypes = new();

    [ObservableProperty]
    private string? _selectedFirmwareType;

    [ObservableProperty]
    private ObservableCollection<string> _availableSerialPorts = new();

    [ObservableProperty]
    private string? _selectedSerialPort;

    public bool IsReadyToFlashFirmware =>
        !string.IsNullOrEmpty(SelectedSerialPort) &&
        !string.IsNullOrEmpty(SelectedFirmwareType) &&
        (!IsWirelessFirmware ||
         (!string.IsNullOrEmpty(WifiSsid) && !string.IsNullOrEmpty(WifiPassword)));

    #endregion

    #region Commands

    public IRelayCommand RefreshSerialPortsCommand { get; }
    public IRelayCommand FlashFirmwareCommand { get; }
    public IRelayCommand DismissWirelessWarningCommand { get; }

    #endregion

    public FirmwareViewModel()
    {
        // Initialize services
        _homePageView = Ioc.Default.GetService<HomePageView>()!;
        _githubService = Ioc.Default.GetRequiredService<GithubService>();
        _firmwareService = Ioc.Default.GetRequiredService<FirmwareService>();
        _settingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _settingsService.Load(this);

        // Initialize commands
        RefreshSerialPortsCommand = new RelayCommand(RefreshSerialPorts);
        FlashFirmwareCommand = new RelayCommand(FlashDeviceFirmware, () => IsReadyToFlashFirmware && !IsFlashing);
        DismissWirelessWarningCommand = new RelayCommand(OnDismissWirelessWarning);

        // Initialize events
        _firmwareService.OnFirmwareUpdateStart += HandleFirmwareUpdateStart;
        _firmwareService.OnFirmwareUpdateError += msg => Debug.WriteLine(msg);
        _firmwareService.OnFirmwareUpdateComplete += HandleFirmwareUpdateComplete;

        // Initial state setup
        RefreshSerialPorts();
        LoadAvailableFirmwareTypesAsync();

        // Setup property changed handlers
        SetupPropertyChangeHandlers();
    }

    private void SetupPropertyChangeHandlers()
    {
        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(SelectedSerialPort):
                    IsDeviceSelected = !string.IsNullOrEmpty(SelectedSerialPort);
                    OnPropertyChanged(nameof(IsReadyToFlashFirmware));
                    break;

                case nameof(SelectedFirmwareType):
                    if (!string.IsNullOrEmpty(SelectedFirmwareType))
                    {
                        IsWirelessFirmware = !SelectedFirmwareType.Contains("Babble_USB");
                        OnPropertyChanged(nameof(IsReadyToFlashFirmware));
                    }
                    break;

                case nameof(WifiSsid):
                case nameof(WifiPassword):
                    if (IsWirelessFirmware)
                    {
                        OnPropertyChanged(nameof(IsReadyToFlashFirmware));
                    }
                    break;
            }

            // Re-evaluate command's CanExecute status
            FlashFirmwareCommand.NotifyCanExecuteChanged();
        };
    }

    #region UI Event Handlers

    private void OnDismissWirelessWarning()
    {
        WirelessWarningVisible = false;
    }

    private void HandleFirmwareUpdateStart()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsFlashing = true;
            IsFinished = false;
            FlashFirmwareCommand.NotifyCanExecuteChanged();
        });
    }

    private void HandleFirmwareUpdateComplete()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsFlashing = false;

            if (IsWirelessFirmware)
            {
                WirelessWarningVisible = true;
            }
            else
            {
                IsFinished = true;
            }

            FlashFirmwareCommand.NotifyCanExecuteChanged();
        });
    }

    #endregion

    #region Operations

    public void RefreshSerialPorts()
    {
        AvailableSerialPorts.Clear();

        foreach (var port in SerialPort.GetPortNames())
        {
            AvailableSerialPorts.Add(port);
        }
    }

    private async void LoadAvailableFirmwareTypesAsync()
    {
        try
        {
            var githubRelease = await _githubService.GetReleases("EyeTrackVR", "OpenIris");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableFirmwareTypes.Clear();
                foreach (var asset in githubRelease.assets)
                {
                    if (asset.name.ToLower().Contains("babble"))
                    {
                        AvailableFirmwareTypes.Add(asset.name);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            // Log or handle the exception
            System.Diagnostics.Debug.WriteLine($"Error loading firmware types: {ex.Message}");
        }
    }

    private async void FlashDeviceFirmware()
    {
        if (string.IsNullOrEmpty(SelectedFirmwareType) || string.IsNullOrEmpty(SelectedSerialPort))
        {
            return;
        }

        string? tempDir = null;
        CancellationTokenSource cts = new CancellationTokenSource();

        try
        {
            await Task.Run(async () =>
            {
                var releases = await _githubService.GetReleases("EyeTrackVR", "OpenIris");
                var asset = releases.assets.FirstOrDefault(a => a.name == SelectedFirmwareType);

                if (asset == null)
                {
                    throw new Exception($"Selected firmware {SelectedFirmwareType} not found");
                }

                tempDir = Directory.CreateTempSubdirectory().FullName;
                var pathToBinary = await _githubService.DownloadAndExtractOpenIrisRelease(
                    tempDir,
                    asset.browser_download_url,
                    asset.name);

                // Stop all cameras before firmware update
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _homePageView.StopLeftCamera(null, null!);
                    _homePageView.StopRightCamera(null, null!);
                    _homePageView.StopFaceCamera(null, null!);
                });

                _firmwareService.UploadFirmware(SelectedSerialPort!, pathToBinary.firmwarePath);

                if (IsWirelessFirmware)
                {
                    // Wait for user to acknowledge reconnection
                    var wirelessReconnectionEvent = new TaskCompletionSource<bool>();

                    // Set up a one-time event handler to catch when warning is dismissed
                    void WirelessWarningHandler(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                    {
                        if (e.PropertyName == nameof(WirelessWarningVisible) && !WirelessWarningVisible)
                        {
                            wirelessReconnectionEvent.SetResult(true);
                            PropertyChanged -= WirelessWarningHandler;
                        }
                    }

                    PropertyChanged += WirelessWarningHandler;

                    // Wait for user to dismiss the warning or for timeout
                    var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
                    var completedTask = await Task.WhenAny(wirelessReconnectionEvent.Task, timeoutTask);

                    // Only send credentials if not timed out and not canceled
                    if (completedTask == wirelessReconnectionEvent.Task && !cts.Token.IsCancellationRequested)
                    {
                        Dispatcher.UIThread.Post(() => IsFlashing = true);
                        _firmwareService.SendWirelessCredentials(SelectedSerialPort!, WifiSsid, WifiPassword);
                        Dispatcher.UIThread.Post(() => IsFlashing = false);
                        Dispatcher.UIThread.Post(() => IsFinished = true);
                    }
                }
            }, cts.Token);
        }
        catch (TaskCanceledException)
        {
            // Operation was canceled, no need for additional handling
        }
        catch (Exception ex)
        {
            // Log or display error to user
            Debug.WriteLine($"Error during firmware update: {ex.Message}");
            IsFlashing = false;
            IsFinished = false;
        }
        finally
        {
            cts.Dispose();

            // Clean up temp directory
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    #endregion

    #region Cleanup

    // Called when the ViewModel is no longer needed
    public void Cleanup()
    {
        _firmwareService.OnFirmwareUpdateStart -= HandleFirmwareUpdateStart;
        _firmwareService.OnFirmwareUpdateComplete -= HandleFirmwareUpdateComplete;
    }

    #endregion
}
