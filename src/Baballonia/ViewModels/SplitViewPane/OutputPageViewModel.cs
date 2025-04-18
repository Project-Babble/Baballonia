using AvaloniaMiaDev.Views;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace AvaloniaMiaDev.ViewModels.SplitViewPane;

public class OutputPageViewModel : ViewModelBase
{
    public OutputPageView View { get; } = Ioc.Default.GetService<OutputPageView>()!;
}
