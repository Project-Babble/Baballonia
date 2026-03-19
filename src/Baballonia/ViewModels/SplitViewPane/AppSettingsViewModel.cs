using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Baballonia.Assets;
using Baballonia.Contracts;
using Baballonia.Services;
using Baballonia.Services.Inference;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using OscCore;
using System;
using System.Collections.ObjectModel;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class AppSettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    [property: SavedSetting("AppSettings_RecalibrateAddress", "/avatar/parameters/etvr_recalibrate")]
    private string _recalibrateAddress;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_RecenterAddress", "/avatar/parameters/etvr_recenter")]
    private string _recenterAddress;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OSCPrefix", "")]
    private string _oscPrefix;

    [ObservableProperty]
    private IBrush _oscPrefixBackgroundColor;

    private bool _isOscPrefixValid = true;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OneEuroEnabled", true)]
    private bool _oneEuroMinEnabled;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OneEuroMinFreqCutoff", 0.5f)]
    private float _oneEuroMinFreqCutoff;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_OneEuroSpeedCutoff", 3f)]
    private float _oneEuroSpeedCutoff;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseDFR", false)]
    private bool _useDFR;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_UseGPU", true)]
    private bool _useGPU;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_SteamVRAutoStart", true)]
    private bool _steamvrAutoStart;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_CheckForUpdates", false)]
    private bool _checkForUpdates;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_ShareEyeData", false)]
    private bool _shareEyeData;

    [ObservableProperty]
    private string _logLevel;

    public ObservableCollection<string> LowestLogLevel { get; } =
    [
        Resources.Settings_LogLevel_Debug,
        Resources.Settings_LogLevel_Information,
        Resources.Settings_LogLevel_Warning,
        Resources.Settings_LogLevel_Error
    ];

    [ObservableProperty]
    [property: SavedSetting("AppSettings_AdvancedOptions", false)]
    private bool _advancedOptions;

    [ObservableProperty]
    [property: SavedSetting("AppSettings_StabilizeEyes", true)]
    private bool _stabilizeEyes;

    [ObservableProperty] private bool _onboardingEnabled;

    public string MachineID => _identityService.GetUniqueUserId();

    public IOscTarget OscTarget { get; }

    private readonly FacePipelineManager _facePipelineManager;
    private readonly EyePipelineManager _eyePipelineManager;
    private readonly IIdentityService _identityService;
    private readonly ILogger<AppSettingsViewModel> _logger;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly OpenVRService _openVrService;

    public AppSettingsViewModel(
        FacePipelineManager facePipelineManager,
        EyePipelineManager eyePipelineManager,
        ILocalSettingsService localSettingsService,
        IOscTarget oscTarget,
        IIdentityService identityService,
        GithubService githubService,
        ParameterSenderService parameterSenderService,
        ILogger<AppSettingsViewModel> logger,
        IThemeSelectorService themeSelectorService)
    {
        OscTarget = oscTarget;
        _localSettingsService = localSettingsService;
        _facePipelineManager = facePipelineManager;
        _eyePipelineManager = eyePipelineManager;
        _identityService = identityService;
        _logger = logger;
        _localSettingsService.Load(this);

        LogLevel = _localSettingsService.ReadSetting("AppSettings_LogLevel", "Debug");

        // Handle edge case where OSC port is used and the system freaks out
        if (OscTarget.OutPort == 0)
        {
            const int port = 8888;
            OscTarget.OutPort = port;
            _localSettingsService.SaveSetting("OSCOutPort", port);
        }

        // Edge case: Update the OscPrefix Background color if and only if
        // The theme changes and the previous input WAS valid (IE keep red)
        themeSelectorService.ThemeChanged += variant =>
        {
            if (_isOscPrefixValid)
                SetOscPrefixBackgroundColor(variant);
        };

        OnboardingEnabled = Utils.IsSupportedDesktopOS;

        PropertyChanged += (_, p) =>
        {
            _localSettingsService.Save(this);
            _facePipelineManager.LoadFilter();
            _eyePipelineManager.LoadFilter();

            if (p.PropertyName == nameof(StabilizeEyes))
            {
                _eyePipelineManager.LoadEyeStabilization();
            }
        };
    }

    partial void OnLogLevelChanged(string value)
    {
        var prev = _localSettingsService.ReadSetting("AppSettings_LogLevel", value);
        if (prev == value)
            return;

        var newLogLevel = value switch
        {
            var v when v == Resources.Settings_LogLevel_Debug => "Debug",
            var v when v == Resources.Settings_LogLevel_Information => "Information",
            var v when v == Resources.Settings_LogLevel_Warning => "Warning",
            var v when v == Resources.Settings_LogLevel_Error => "Error",
            _ => "Debug"
        };
        _localSettingsService.SaveSetting("AppSettings_LogLevel", newLogLevel);
    }

    partial void OnOscPrefixChanged(string value)
    {
        // 1) A valid OSC prefix is also a valid message itself
        // IE: /foo/bar + /cheekPuffLeft
        // 2) Empty strings are also valid, IE no prefix
        _isOscPrefixValid = OscMessage.TryParse(value, out _) || string.IsNullOrEmpty(value);

        if (_isOscPrefixValid)
        {
            _localSettingsService.SaveSetting("AppSettings_OSCPrefix", value);
            SetOscPrefixBackgroundColor(Application.Current!.ActualThemeVariant);
            return;
        }

        OscPrefixBackgroundColor = new SolidColorBrush(Colors.PaleVioletRed);
    }

    private void SetOscPrefixBackgroundColor(ThemeVariant theme)
    {
        // Workaround to get proper SystemChromeMediumColor color
        OscPrefixBackgroundColor = theme.ToString() switch
        {
            "Light" => new SolidColorBrush(Colors.White),
            "Dark" => SolidColorBrush.Parse("#ff202020"),
            _ => OscPrefixBackgroundColor
        };
    }

    partial void OnSteamvrAutoStartChanged(bool value)
    {
        var readValue = _localSettingsService.ReadSetting("AppSettings_SteamVRAutoStart", value);
        if (readValue == value || _openVrService == null)
            return;

        try
        {
            _openVrService.SteamvrAutoStart = value;
            _localSettingsService.SaveSetting("AppSettings_SteamVRAutoStart", value);
        }
        catch (Exception e)
        {
            _logger.LogError("DLL not found!", e);
        }
    }

    async partial void OnUseGPUChanged(bool value)
    {
        var prev = _localSettingsService.ReadSetting("AppSettings_UseGPU", value);
        if (prev == value)
            return;

        try
        {
            _localSettingsService.SaveSetting("AppSettings_UseGPU", value);
            var loadFace = _eyePipelineManager.LoadInferenceAsync();
            var loadEye = _facePipelineManager.LoadInferenceAsync();

            await loadEye;
            await loadFace;
        }
        catch (Exception e)
        {
            _logger.LogError("", e);
        }
    }
}
