using System.Globalization;
using System.Threading.Tasks;
using Baballonia.Contracts;

namespace Baballonia.Services;

public class LanguageSelectorService(ILocalSettingsService localSettingsService) : ILanguageSelectorService
{
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
        Language = language;
        SetRequestedLanguage();
        SaveLanguageInSettings(Language);
    }

    public void SetRequestedLanguage()
    {
        Assets.Resources.Culture = new CultureInfo(Language == DefaultLanguage ?
            CultureInfo.CurrentCulture.TwoLetterISOLanguageName :
            Language);
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
