using System;

namespace Baballonia.Contracts;

public interface IPageService
{
    Type GetPageType(string key);
}
