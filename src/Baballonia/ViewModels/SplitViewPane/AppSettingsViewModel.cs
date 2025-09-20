using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Logging;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.Services.Inference.Filters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        OscTarget = Ioc.Default.GetService<IOscTarget>()!;
        GithubService = Ioc.Default.GetService<GithubService>()!;
        SettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _processingLoopService = Ioc.Default.GetService<ProcessingLoopService>()!;
        _logger = Ioc.Default.GetService<ILogger<AppSettingsViewModel>>()!;
        SettingsService.Load(this);
        if (OscTarget.OutPort == 0)
        {
            const int Port = 8888;
            OscTarget.OutPort = Port;
            SettingsService.SaveSetting("OSCOutPort", Port);
        }

        ParameterSenderService = Ioc.Default.GetService<ParameterSenderService>()!;

        OnboardingEnabled = Utils.IsSupportedDesktopOS;

        PropertyChanged += (_, _) =>
        {
            SettingsService.Save(this);
        };
    }

    async partial void OnUseGPUChanged(bool value)
    {
        var prev = SettingsService.ReadSetting("AppSettings_UseGPU", value);
        if (prev == value)
            return;

        try
        {
            SettingsService.SaveSetting("AppSettings_UseGPU", value);
            var face = _processingLoopService.LoadFaceInferenceAsync();
            var eyes = _processingLoopService.LoadEyeInferenceAsync();

            _processingLoopService.FaceProcessingPipeline.InferenceService = await face;
            _processingLoopService.EyesProcessingPipeline.InferenceService = await eyes;
        }
        catch (Exception e)
        {
            _logger.LogError("", e);
        }
    }
}
