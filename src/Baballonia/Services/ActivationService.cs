using System.Threading.Tasks;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class ActivationService(
    IThemeSelectorService themeSelectorService,
    ILanguageSelectorService languageSelectorService)
    : IActivationService
{
    public async Task ActivateAsync(object activationArgs)
    {
        await languageSelectorService.InitializeAsync();
        await languageSelectorService.SetRequestedLanguageAsync();
        await themeSelectorService.InitializeAsync();
        await themeSelectorService.SetRequestedThemeAsync();
    }
}
