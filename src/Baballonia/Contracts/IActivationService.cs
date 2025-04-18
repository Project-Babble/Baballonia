using System.Threading.Tasks;

namespace AvaloniaMiaDev.Contracts;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}
