using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.Services.Inference.Filters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class AppSettingsViewModel : ViewModelBase
{
    public IOscTarget OscTarget { get; private set;}
    public ILocalSettingsService SettingsService { get; }
    public GithubService GithubService { get; private set;}
    public ParameterSenderService ParameterSenderService { get; private set;}

    [ObservableProperty]
    [property: SavedSetting("AppSettings_RecalibrateAddress", "/avatar/parameters/etvr_recalibrate")]
    private string _recalibrateAddress;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_RecenterAddress", "/avatar/parameters/etvr_recenter")]
    private string _recenterAddress;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseOSCQuery", false)]
    private bool _useOscQuery;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OSCPrefix", "")]
    private string _oscPrefix;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OneEuroEnabled", false)]
    private bool _oneEuroMinEnabled;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OneEuroMinFreqCutoff", 1f)]
    private float _oneEuroMinFreqCutoff;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OneEuroSpeedCutoff", 1f)]
    private float _oneEuroSpeedCutoff;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseGPU", true)]
    private bool _useGPU;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_CheckForUpdates", false)]
    private bool _checkForUpdates;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_LogLevel", "Debug")]
    private string _logLevel;

    public List<string> LowestLogLevel { get; } =
    [
        "Debug",
        "Information",
        "Warning",
        "Error"
    ];

    [ObservableProperty] private bool _onboardingEnabled;

    private ProcessingLoopService _processingLoopService;
    public AppSettingsViewModel()
    {
        // General/Calibration Settings
        OscTarget = Ioc.Default.GetService<IOscTarget>()!;
        GithubService = Ioc.Default.GetService<GithubService>()!;
        SettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _processingLoopService = Ioc.Default.GetService<ProcessingLoopService>()!;
        SettingsService.Load(this);

        // Handle edge case where OSC port is used and the system freaks out
        if (OscTarget.OutPort == 0)
        {
            const int Port = 8888;
            OscTarget.OutPort = Port;
            Task.Run(async () => await SettingsService.SaveSettingAsync("OSCOutPort", Port));
        }

        // Risky Settings
        ParameterSenderService = Ioc.Default.GetService<ParameterSenderService>()!;

        OnboardingEnabled = Utils.IsSupportedDesktopOS;

        PropertyChanged += (_, _) =>
        {
            if (!_oneEuroMinEnabled)
            {
                _processingLoopService.FaceProcessingPipeline.Filter = null;
                _processingLoopService.EyesProcessingPipeline.Filter = null;
            }
            else
            {
                float[] faceArray = new float[Utils.FaceRawExpressions];
                var faceFilter = new OneEuroFilter(
                    faceArray,
                    minCutoff: _oneEuroMinFreqCutoff,
                    beta: _oneEuroSpeedCutoff
                );
                float[] eyeArray = new float[Utils.EyeRawExpressions];
                var eyeFilter = new OneEuroFilter(
                    eyeArray,
                    minCutoff: _oneEuroMinFreqCutoff,
                    beta: _oneEuroSpeedCutoff
                );
                _processingLoopService.FaceProcessingPipeline.Filter = faceFilter;
                _processingLoopService.EyesProcessingPipeline.Filter = eyeFilter;
            }

            SettingsService.Save(this);
        };
    }

    partial void OnUseGPUChanged(bool value)
    {
        Task.Run(async () =>
        {
            await SettingsService.SaveSettingAsync("AppSettings_UseGPU", value);
            await _processingLoopService.SetupFaceInference();
            await _processingLoopService.SetupEyeInference();
        });
    }
}
