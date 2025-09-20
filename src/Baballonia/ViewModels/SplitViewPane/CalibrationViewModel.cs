using CommunityToolkit.Mvvm.ComponentModel;
using Baballonia.Models;
using Baballonia.Contracts;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baballonia.Helpers;
using Baballonia.Services;
using CommunityToolkit.Mvvm.Input;
using Baballonia.Services.Inference.Filters;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class CalibrationViewModel : ViewModelBase, IDisposable
{
    public ParameterGroupCollection EyeMovementSettings { get; set; }
    public ParameterGroupCollection EyeBlinkingSettings { get; set; }
    public ParameterGroupCollection JawSettings { get; set; }
    public ParameterGroupCollection MouthSettings { get; set; }
    public ParameterGroupCollection TongueSettings { get; set; }
    public ParameterGroupCollection NoseSettings { get; set; }
    public ParameterGroupCollection CheekSettings { get; set; }

    [ObservableProperty]
    [property: SavedSetting("AppSettings_StabilizeEyes", false)]
    private bool _stabilizeEyes;

    private ILocalSettingsService _settingsService { get; }
    private readonly ICalibrationService _calibrationService;
    private readonly ParameterSenderService _parameterSenderService;
    private readonly ProcessingLoopService _processingLoopService;

    private readonly Dictionary<string, int> _eyeKeyIndexMap;
    private readonly Dictionary<string, int> _faceKeyIndexMap;
    
    public CalibrationViewModel()
    {
        _settingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _calibrationService = Ioc.Default.GetService<ICalibrationService>()!;
        _parameterSenderService = Ioc.Default.GetService<ParameterSenderService>()!;
        _processingLoopService = Ioc.Default.GetService<ProcessingLoopService>()!;

        EyeMovementSettings = new ParameterGroupCollection("EyeMovement", new GroupFilterSettings(_settingsService, "Filter_EyeMovement", false, 0.1f, 0.1f),
        [
            new("LeftEyeX", -1f, 1f, -1f, 1f),
            new("LeftEyeY", -1f, 1f, -1f, 1f),
            new("RightEyeX", -1f, 1f, -1f, 1f),
            new("RightEyeY", -1f, 1f, -1f, 1f)
        ]);

        EyeBlinkingSettings = new ParameterGroupCollection("EyeBlinking", new GroupFilterSettings(_settingsService, "Filter_EyeBlinking", false, 0.5f, 0.5f),
        [
            new("LeftEyeLid"),
            new("RightEyeLid")
        ]);

        JawSettings = new ParameterGroupCollection("Jaw", new GroupFilterSettings(_settingsService, "Filter_Jaw", false, 1.0f, 1.0f),
        [
            new("JawOpen"),
            new("JawForward"),
            new("JawLeft"),
            new("JawRight")
        ]);

        CheekSettings = new ParameterGroupCollection("Cheek", new GroupFilterSettings(_settingsService, "Filter_Cheek", false, 1.0f, 1.0f),
        [
            new("CheekPuffLeft"),
            new("CheekPuffRight"),
            new("CheekSuckLeft"),
            new("CheekSuckRight")
        ]);

        NoseSettings = new ParameterGroupCollection("Nose", new GroupFilterSettings(_settingsService, "Filter_Nose", false, 1.0f, 1.0f),
        [
            new("NoseSneerLeft"),
            new("NoseSneerRight")
        ]);

        MouthSettings = new ParameterGroupCollection("Mouth", new GroupFilterSettings(_settingsService, "Filter_Mouth", false, 1.0f, 1.0f),
        [
            new("MouthFunnel"),
            new("MouthPucker"),
            new("MouthLeft"),
            new("MouthRight"),
            new("MouthRollUpper"),
            new("MouthRollLower"),
            new("MouthShrugUpper"),
            new("MouthShrugLower"),
            new("MouthClose"),
            new("MouthSmileLeft"),
            new("MouthSmileRight"),
            new("MouthFrownLeft"),
            new("MouthFrownRight"),
            new("MouthDimpleLeft"),
            new("MouthDimpleRight"),
            new("MouthUpperUpLeft"),
            new("MouthUpperUpRight"),
            new("MouthLowerDownLeft"),
            new("MouthLowerDownRight"),
            new("MouthPressLeft"),
            new("MouthPressRight"),
            new("MouthStretchLeft"),
            new("MouthStretchRight")
        ]);

        TongueSettings = new ParameterGroupCollection("Tongue", new GroupFilterSettings(_settingsService, "Filter_Tongue", false, 1.0f, 1.0f),
        [
            new("TongueOut"),
            new("TongueUp"),
            new("TongueDown"),
            new("TongueLeft"),
            new("TongueRight"),
            new("TongueRoll"),
            new("TongueBendDown"),
            new("TongueCurlUp"),
            new("TongueSquish"),
            new("TongueFlat"),
            new("TongueTwistLeft"),
            new("TongueTwistRight")
        ]);

        foreach (var setting in EyeMovementSettings.Concat(EyeBlinkingSettings).Concat(JawSettings).Concat(CheekSettings)
                     .Concat(NoseSettings).Concat(MouthSettings).Concat(TongueSettings))
        {
            setting.PropertyChanged += OnSettingChanged;
        }

        var allParameterGroups = new[] { EyeMovementSettings, EyeBlinkingSettings, JawSettings, 
                                        MouthSettings, TongueSettings, NoseSettings, CheekSettings };
        foreach (var group in allParameterGroups)
        {
            group.FilterSettings.PropertyChanged += OnFilterSettingChanged;
        }

        _eyeKeyIndexMap = _parameterSenderService.EyeExpressionMap.Keys
            .Select((key, index) => new { key, index })
            .ToDictionary(x => x.key, x => x.index);

        _faceKeyIndexMap = _parameterSenderService.FaceExpressionMap.Keys
            .Select((key, index) => new { key, index })
            .ToDictionary(x => x.key, x => x.index);

        PropertyChanged += (o, p) =>
        {
            var propertyInfo = GetType().GetProperty(p.PropertyName!);
            object value = propertyInfo?.GetValue(this)!;
            if (value is float floatValue)
            {
                if (p.PropertyName == null) return;
                _calibrationService.SetExpression(p.PropertyName!, floatValue);
            }
        };

        _processingLoopService.ExpressionChangeEvent += ExpressionUpdateHandler;

        LoadInitialSettings();
        UpdateProcessingFilters();
        _settingsService.Load(this);

        PropertyChanged += (o, p) =>
        {
            if (p.PropertyName == nameof(StabilizeEyes))
            {
                _settingsService.SaveSetting("AppSettings_StabilizeEyes", StabilizeEyes);
                _processingLoopService.EyesProcessingPipeline.StabilizeEyes = StabilizeEyes;
            }
        };

    }

    private void ExpressionUpdateHandler(ProcessingLoopService.Expressions expressions)
    {
        if(expressions.FaceExpression != null)
            Dispatcher.UIThread.Post(() =>
            {
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, CheekSettings);
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, MouthSettings);
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, JawSettings);
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, NoseSettings);
                ApplyCurrentFaceExpressionValues(expressions.FaceExpression, TongueSettings);
            });
        if(expressions.EyeExpression != null)
            Dispatcher.UIThread.Post(() =>
            {
                ApplyCurrentEyeExpressionValues(expressions.EyeExpression, EyeMovementSettings);
                ApplyCurrentEyeExpressionValues(expressions.EyeExpression, EyeBlinkingSettings);
            });
    }
    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SliderBindableSetting setting) return;

        if (e.PropertyName is nameof(SliderBindableSetting.Lower))
        {
            _calibrationService.SetExpression(setting.Name + "Lower", setting.Lower);
        }

        if (e.PropertyName is nameof(SliderBindableSetting.Upper))
        {
            _calibrationService.SetExpression(setting.Name + "Upper", setting.Upper);
        }
    }

    private void ApplyCurrentEyeExpressionValues(float[] values, ParameterGroupCollection settings)
    {
        foreach (var setting in settings)
        {
            if (_eyeKeyIndexMap.TryGetValue(setting.Name, out var index)
                && index < values.Length)
            {
                var weight = values[index];
                var val = Math.Clamp(
                    weight.Remap(setting.Lower, setting.Upper, setting.Min, setting.Max),
                    setting.Min,
                    setting.Max);
                setting.CurrentExpression = val;
            }
        }
    }

    private void ApplyCurrentFaceExpressionValues(float[] values, ParameterGroupCollection settings)
    {
        foreach (var setting in settings)
        {
            if (_faceKeyIndexMap.TryGetValue(setting.Name, out var index)
                && index < values.Length)
            {
                var weight = values[index];
                var val = Math.Clamp(
                    weight.Remap(setting.Lower, setting.Upper, setting.Min, setting.Max),
                    setting.Min,
                    setting.Max);
                setting.CurrentExpression = val;
            }
        }
    }

    [RelayCommand]
    public void ResetMinimums()
    {
        _calibrationService.ResetMinimums();
        LoadInitialSettings();
    }

    [RelayCommand]
    public void ResetMaximums()
    {
        _calibrationService.ResetMaximums();
        LoadInitialSettings();
    }

    private void LoadInitialSettings()
    {
        LoadInitialSettings(EyeMovementSettings);
        LoadInitialSettings(EyeBlinkingSettings);
        LoadInitialSettings(CheekSettings);
        LoadInitialSettings(JawSettings);
        LoadInitialSettings(MouthSettings);
        LoadInitialSettings(NoseSettings);
        LoadInitialSettings(TongueSettings);
    }

    private void LoadInitialSettings(IEnumerable<SliderBindableSetting> settings)
    {
        foreach (var setting in settings)
        {
            var val = _calibrationService.GetExpressionSettings(setting.Name);
            setting.Lower = val.Lower;
            setting.Upper = val.Upper;
            setting.Min = val.Min;
            setting.Max = val.Max;
        }
    }

    private void OnFilterSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateProcessingFilters();
    }

    private void UpdateProcessingFilters()
    {
        var faceGroupFilter = new GroupedOneEuroFilter();
        var eyeGroupFilter = new GroupedOneEuroFilter();

        // Configure eye tracking filters
        if (EyeMovementSettings.FilterSettings.Enabled)
        {
            var eyeMovementIndices = GetEyeParameterIndices(EyeMovementSettings);
            if (eyeMovementIndices.Length > 0)
            {
                eyeGroupFilter.ConfigureGroup("EyeMovement", eyeMovementIndices,
                    EyeMovementSettings.FilterSettings.MinFreqCutoff, EyeMovementSettings.FilterSettings.SpeedCutoff);
            }
        }

        if (EyeBlinkingSettings.FilterSettings.Enabled)
        {
            var eyeBlinkingIndices = GetEyeParameterIndices(EyeBlinkingSettings);
            if (eyeBlinkingIndices.Length > 0)
            {
                eyeGroupFilter.ConfigureGroup("EyeBlinking", eyeBlinkingIndices,
                    EyeBlinkingSettings.FilterSettings.MinFreqCutoff, EyeBlinkingSettings.FilterSettings.SpeedCutoff);
            }
        }

        // Configure face tracking filters
        var faceParameterGroups = new[] { JawSettings, MouthSettings, TongueSettings, NoseSettings, CheekSettings };
        foreach (var group in faceParameterGroups)
        {
            if (group.FilterSettings.Enabled)
            {
                var indices = GetFaceParameterIndices(group);
                if (indices.Length > 0)
                {
                    faceGroupFilter.ConfigureGroup(group.GroupName, indices,
                        group.FilterSettings.MinFreqCutoff, group.FilterSettings.SpeedCutoff);
                }
            }
        }

        _processingLoopService.FaceProcessingPipeline.Filter = faceGroupFilter;
        _processingLoopService.EyesProcessingPipeline.Filter = eyeGroupFilter;
    }

    private int[] GetFaceParameterIndices(ParameterGroupCollection group)
    {
        return group.Select(setting => _faceKeyIndexMap.TryGetValue(setting.Name, out var index) ? index : -1)
                   .Where(index => index >= 0)
                   .ToArray();
    }

    private int[] GetEyeParameterIndices(ParameterGroupCollection group)
    {
        return group.Select(setting => _eyeKeyIndexMap.TryGetValue(setting.Name, out var index) ? index : -1)
                   .Where(index => index >= 0)
                   .ToArray();
    }

    public void Dispose()
    {
        _processingLoopService.ExpressionChangeEvent -= ExpressionUpdateHandler;

        var allParameterGroups = new[] { EyeMovementSettings, EyeBlinkingSettings, JawSettings, 
                                        MouthSettings, TongueSettings, NoseSettings, CheekSettings };
        foreach (var group in allParameterGroups)
        {
            group.FilterSettings.PropertyChanged -= OnFilterSettingChanged;
        }
    }
}
