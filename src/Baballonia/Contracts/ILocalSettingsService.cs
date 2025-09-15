using System;
using System.Threading.Tasks;

namespace Baballonia.Contracts;

public class SavedSettingAttribute : Attribute
{
    private readonly string _settingName;
    private readonly object? _defaultValue;
    private readonly bool _forceLocal;

    public SavedSettingAttribute(string settingName, object? defaultValue = default, bool forceLocal = false)
    {
        _settingName = settingName;
        _defaultValue = defaultValue;
        _forceLocal = forceLocal;
    }

    public string GetName() => _settingName;
    public object? Default() => _defaultValue;
    public bool ForceLocal() => _forceLocal;
}

public interface ILocalSettingsService
{
    T ReadSetting<T>(string key, T? defaultValue = default, bool forceLocal = false);

    void SaveSetting<T>(string key, T value, bool forceLocal = false);

    void Save(object target);
    void Load(object target);
    void ForceSave();
}
