using System.Threading.Tasks;

namespace Baballonia.Contracts;

public interface IActivationService
{
    void Activate(object activationArgs);
}
