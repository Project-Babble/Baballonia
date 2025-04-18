using System.Threading.Tasks;

namespace AvaloniaMiaDev.Activation;

// Extend this class to implement new ActivationHandlers. See DefaultActivationHandler for an example.
// https://github.com/microsoft/TemplateStudio/blob/main/docs/WinUI/activation.md
public abstract class ActivationHandler : IActivationHandler
{
    public bool CanHandle(object args)
    {
        return true;
    }

    public Task HandleAsync(object args)
    {
        return Task.CompletedTask;
    }
}
