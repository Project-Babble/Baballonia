using System;

namespace AvaloniaMiaDev.Contracts;

public interface IPageService
{
    Type GetPageType(string key);
}
