using System.Threading.Tasks;

namespace AvaloniaMiaDev.Activation;

public interface IActivationHandler
{
    bool CanHandle(object args);

    Task HandleAsync(object args);
}
