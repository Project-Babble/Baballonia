using System.Threading.Tasks;

namespace Baballonia.Activation;

public interface IActivationHandler
{
    bool CanHandle(object args);

    Task HandleAsync(object args);
}
