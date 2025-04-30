using System.Threading.Tasks;
using Avalonia.Controls;
using Baballonia.Views;

namespace Baballonia.Helpers
{
    public static class MainWindowExtensions
    {
        public static void ShowOnboardingIfNeeded(this Window mainWindow)
        {
            OnboardingView.ShowIfNeeded(mainWindow);
        }

        public static void ShowOnboarding(this Window mainWindow)
        {
            OnboardingView.ShowOnboarding(mainWindow);
        }
    }
}
