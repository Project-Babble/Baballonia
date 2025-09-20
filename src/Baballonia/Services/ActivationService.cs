using System.Threading.Tasks;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class ActivationService(
    IThemeSelectorService themeSelectorService,
    ILanguageSelectorService languageSelectorService)
    : IActivationService
{
    public void Activate(object activationArgs)
    {
        languageSelectorService.Initialize();
        languageSelectorService.SetRequestedLanguage();
        themeSelectorService.Initialize();
        themeSelectorService.SetRequestedTheme();
    }
}
