using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class FirmwareViewModel : ViewModelBase
{
    private readonly HomePageView _homePageView;
    private readonly GithubService _githubService;
    private readonly FirmwareService _firmwareService;
    private GithubRelease _githubRelease;

    public bool IsDeviceSelected { get; private set; }
    public bool IsWirelessFirmware { get; private set; }

    [ObservableProperty]
    public bool _isReadyToFlashFirmware;

    [ObservableProperty]
    public bool _isFlashing;

    [ObservableProperty]
    public bool _isFinished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadyToFlashFirmware))]
    private string _wifiSsid = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadyToFlashFirmware))]
    private string _wifiPassword = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _availableFirmwareTypes = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWirelessFirmware))]
    [NotifyPropertyChangedFor(nameof(IsReadyToFlashFirmware))]
    private string? _selectedFirmwareType;

    [ObservableProperty]
    private ObservableCollection<string> _availableSerialPorts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeviceSelected))]
    [NotifyPropertyChangedFor(nameof(IsReadyToFlashFirmware))]
    private string? _selectedSerialPort = string.Empty;

    public ICommand ProgramCommand { get; }
    public ICommand FlashFirmware { get; }

    public FirmwareViewModel()
    {
        _homePageView = Ioc.Default.GetService<HomePageView>()!;
        _githubService = Ioc.Default.GetRequiredService<GithubService>();
        _firmwareService = Ioc.Default.GetRequiredService<FirmwareService>();
        ProgramCommand = new RelayCommand(UpdateSerialPorts);
        FlashFirmware = new RelayCommand(FlashDeviceFirmware);
        UpdateSerialPorts();

        IsFinished = false;
        IsFlashing = false;
        _firmwareService.OnFirmwareUpdateStart += async () =>
        {
            IsFlashing = true;
            IsFinished = false;

        };
        _firmwareService.OnFirmwareUpdateComplete += async () =>
        {
            IsFlashing = false;
            IsFinished = true;
        };

        Task.Run(async () =>
        {
            _githubRelease = await _githubService.GetReleases("EyeTrackVR", "OpenIris");
            foreach (var asset in _githubRelease.assets)
            {
                if (asset.name.ToLower().Contains("babble"))
                {
                    AvailableFirmwareTypes.Add(asset.name);
                }
            }
        });

        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(SelectedSerialPort) when string.IsNullOrEmpty(SelectedSerialPort):
                    IsDeviceSelected = false;
                    return;
                case nameof(SelectedSerialPort):
                    IsDeviceSelected = true;
                    break;
                case nameof(SelectedFirmwareType) when string.IsNullOrEmpty(SelectedFirmwareType):
                    return;
                case nameof(SelectedFirmwareType):
                    IsWirelessFirmware = !SelectedFirmwareType!.Contains("Babble_USB");
                    break;
            }

            if (IsWirelessFirmware)
            {
                _isReadyToFlashFirmware =
                    !string.IsNullOrEmpty(SelectedSerialPort) &&
                    !string.IsNullOrEmpty(SelectedFirmwareType) &&
                    !string.IsNullOrEmpty(WifiSsid) &&
                    !string.IsNullOrEmpty(WifiPassword);
            }
            else
            {
                _isReadyToFlashFirmware =
                    !string.IsNullOrEmpty(SelectedSerialPort) &&
                    !string.IsNullOrEmpty(SelectedFirmwareType);
            }
        };
    }

    private void UpdateSerialPorts()
    {
        AvailableSerialPorts.Clear();
        foreach (var name in SerialPort.GetPortNames())
        {
            AvailableSerialPorts.Add(name);
        }
    }

    private async void FlashDeviceFirmware()
    {
        await Task.Run(async () =>
        {
            var releases = await _githubService.GetReleases("EyeTrackVR", "OpenIris");
            var asset = releases.assets.Where(asset => asset.name == SelectedFirmwareType)!.First();
            var tempDir = Directory.CreateTempSubdirectory().FullName;
            var pathToBinary = await _githubService.DownloadAndExtractOpenIrisRelease(tempDir, asset.browser_download_url, asset.name);

            _homePageView.StopLeftCamera(null, null!);
            _homePageView.StopRightCamera(null, null!);
            _homePageView.StopFaceCamera(null, null!);

            await _firmwareService.UploadFirmwareAsync(_selectedSerialPort!, pathToBinary.firmwarePath);
            Directory.Delete(tempDir, true);
        });
    }
}
