using Baballonia.Contracts;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baballonia.Services;

public class ActivationService(
    IThemeSelectorService themeSelectorService,
    ILanguageSelectorService languageSelectorService,
    ILogger<ActivationService> logger)
    : IActivationService
{
    public void Activate(object activationArgs)
    {
        languageSelectorService.Initialize();
        languageSelectorService.SetRequestedLanguage();
        themeSelectorService.Initialize();
        themeSelectorService.SetRequestedTheme();

        // Guard against mobile
        if (!Utils.IsSupportedDesktopOS) return;

        // Checking to see if AutoStart has checks pass during service activation
        var openVrService = Ioc.Default.GetService<OpenVRService>();
        logger.LogInformation("Configuring OpenVR...");
        if (!openVrService!.AutoStart())
        {
            logger.LogWarning("Failed to configure OpenVR during ActivationService startup. Skipping.");
        }
    }
}
