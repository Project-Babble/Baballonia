using System.Threading.Tasks;

namespace Baballonia.Contracts;

public interface ILanguageSelectorService
{
    string Language
    {
        get;
    }

    void Initialize();

    void SetLanguage(string language);

    void SetRequestedLanguage();
}
