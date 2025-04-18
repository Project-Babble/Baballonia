using System.Threading.Tasks;
using Avalonia.Styling;

namespace AvaloniaMiaDev.Contracts;

public interface IThemeSelectorService
{
    ThemeVariant Theme
    {
        get;
    }

    Task InitializeAsync();

    Task SetThemeAsync(ThemeVariant theme);

    Task SetRequestedThemeAsync();
}
