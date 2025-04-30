using System.Threading.Tasks;

namespace Baballonia.Contracts;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}
