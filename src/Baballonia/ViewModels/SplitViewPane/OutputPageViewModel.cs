using Baballonia.Views;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.ViewModels.SplitViewPane;

public class OutputPageViewModel : ViewModelBase
{
    public OutputPageView View { get; } = Ioc.Default.GetService<OutputPageView>()!;
}
