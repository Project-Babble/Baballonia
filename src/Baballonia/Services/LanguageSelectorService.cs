using System.Globalization;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Jeek.Avalonia.Localization;

namespace Baballonia.Services;

public class LanguageSelectorService(ILocalSettingsService localSettingsService) : ILanguageSelectorService
{
    public const string DefaultLanguage = "DefaultLanguage";

    private const string SettingsKey = "AppBackgroundRequestedLanguage";

    public string Language { get; set; } = DefaultLanguage;

    public async Task InitializeAsync()
    {
        Language = await LoadLanguageFromSettingsAsync();
        await SetRequestedLanguageAsync();
        await Task.CompletedTask;
    }

    public async Task SetLanguageAsync(string language)
    {
        Language = language;
        await SetRequestedLanguageAsync();
        await SaveLanguageInSettingsAsync(Language);
    }

    public Task SetRequestedLanguageAsync()
    {
        Localizer.Language = Language == DefaultLanguage ?
            CultureInfo.CurrentCulture.TwoLetterISOLanguageName :
            Language;
        return Task.CompletedTask;
    }

    private async Task<string> LoadLanguageFromSettingsAsync()
    {
        return await localSettingsService.ReadSettingAsync<string>(SettingsKey);;
    }

    private async Task SaveLanguageInSettingsAsync(string langauge)
    {
        await localSettingsService.SaveSettingAsync(SettingsKey, langauge);
    }
}
