using System;

namespace Baballonia.Contracts;

public interface ILanguageSelectorService
{
    event Action OnLanguageUpdated;

    string Language
    {
        get;
    }

    void Initialize();

    void SetLanguage(string language);

    void SetRequestedLanguage();
}
