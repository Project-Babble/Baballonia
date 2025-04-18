using System;
using System.Threading.Tasks;

namespace AvaloniaMiaDev.Contracts;
public interface IMainService
{
    Action<string, float> ParameterUpdate { get; set; }

    Task Teardown();
    Task InitializeAsync();
}
