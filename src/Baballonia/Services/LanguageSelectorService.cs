using Baballonia.Contracts;
using System;
using System.Globalization;

namespace Baballonia.Services;

public class LanguageSelectorService(ILocalSettingsService localSettingsService) : ILanguageSelectorService
{
    public event Action OnLanguageUpdated;

    public const string DefaultLanguage = "DefaultLanguage";

    private const string SettingsKey = "AppBackgroundRequestedLanguage";

    public string Language { get; set; } = DefaultLanguage;

    public void Initialize()
    {
        Language = LoadLanguageFromSettings();
        SetRequestedLanguage();
    }

    public void SetLanguage(string language)
    {
        OnLanguageUpdated?.Invoke();
        Language = language;
        SetRequestedLanguage();
        SaveLanguageInSettings(Language);
    }

    public void SetRequestedLanguage()
    {
        // Use full culture name (eg. "zh-CN") so ResourceManager finds the specific satellite
        // assembly. Fall back to the current UI culture name when DefaultLanguage is requested.
        var cultureName = Language == DefaultLanguage ? CultureInfo.CurrentUICulture.Name : Language;
        Assets.Resources.Culture = new CultureInfo(cultureName);
    }

    private string LoadLanguageFromSettings()
    {
        return localSettingsService.ReadSetting<string>(SettingsKey);;
    }

    private void SaveLanguageInSettings(string langauge)
    {
        localSettingsService.SaveSetting(SettingsKey, langauge);
    }
}
