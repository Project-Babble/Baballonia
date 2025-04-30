using System.Threading.Tasks;

namespace Baballonia.Contracts;

public interface ILanguageSelectorService
{
    string Language
    {
        get;
    }

    Task InitializeAsync();

    Task SetLanguageAsync(string language);

    Task SetRequestedLanguageAsync();
}
