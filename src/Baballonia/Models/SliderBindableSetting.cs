using CommunityToolkit.Mvvm.ComponentModel;
using Baballonia.Contracts;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;

namespace Baballonia.Models;

public partial class SliderBindableSetting : ObservableObject
{
    public string Name { get; set; }

    [ObservableProperty] private float _lower;
    [ObservableProperty] private float _currentExpression;

    [ObservableProperty] private float _upper;
    [ObservableProperty] private float _min;
    [ObservableProperty] private float _max;

    public SliderBindableSetting(string name, float lower = 0f, float upper = 1f, float min = 0f, float max = 1f)
    {
        Name = name;
        Lower = lower;
        Upper = upper;
        Min = min;
        Max = max;
    }
}

public partial class ParameterGroupCollection(
    string groupName,
    IFilterSettings filterSettings,
    IEnumerable<SliderBindableSetting> items)
    : ObservableCollection<SliderBindableSetting>(items)
{
    public string GroupName { get; } = groupName;
    public IFilterSettings FilterSettings { get; } = filterSettings;
}

public interface IFilterSettings : INotifyPropertyChanged
{
    bool Enabled { get; set; }
    float MinFreqCutoff { get; set; }
    float SpeedCutoff { get; set; }
}

public partial class GroupFilterSettings : ObservableObject, IFilterSettings
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly string _prefix;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private float _minFreqCutoff;

    [ObservableProperty]
    private float _speedCutoff;

    public GroupFilterSettings(ILocalSettingsService localSettingsService, string settingPrefix,
        bool defaultEnabled, float defaultMinFreqCutoff, float defaultSpeedCutoff)
    {
        _localSettingsService = localSettingsService;
        _prefix = settingPrefix;

        Enabled = defaultEnabled;
        MinFreqCutoff = defaultMinFreqCutoff;
        SpeedCutoff = defaultSpeedCutoff;

        var enabled = _localSettingsService.ReadSetting($"{_prefix}_Enabled", defaultEnabled);
        var min = _localSettingsService.ReadSetting($"{_prefix}_MinFreq", defaultMinFreqCutoff);
        var speed = _localSettingsService.ReadSetting($"{_prefix}_Speed", defaultSpeedCutoff);

        Dispatcher.UIThread.Post(() =>
        {
            Enabled = enabled;
            MinFreqCutoff = min;
            SpeedCutoff = speed;
        });

        PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(Enabled):
                    _localSettingsService.SaveSetting($"{_prefix}_Enabled", Enabled);
                    break;
                case nameof(MinFreqCutoff):
                    _localSettingsService.SaveSetting($"{_prefix}_MinFreq", MinFreqCutoff);
                    break;
                case nameof(SpeedCutoff):
                    _localSettingsService.SaveSetting($"{_prefix}_Speed", SpeedCutoff);
                    break;
            }
        };
    }
}
