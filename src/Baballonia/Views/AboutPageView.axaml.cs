using Baballonia.ViewModels.SplitViewPane;

namespace Baballonia.Views;

public partial class AboutPageView : ViewBase
{
    public AboutPageView()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is AboutPageViewModel viewModel)
            {
                await viewModel.LoadContributorsAsync();
            }
        };
    }
}
