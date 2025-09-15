using System.Threading.Tasks;
using Avalonia.Styling;

namespace Baballonia.Contracts;

public interface IThemeSelectorService
{
    ThemeVariant Theme
    {
        get;
    }

    void Initialize();

    void SetTheme(ThemeVariant theme);

    void SetRequestedTheme();
}
