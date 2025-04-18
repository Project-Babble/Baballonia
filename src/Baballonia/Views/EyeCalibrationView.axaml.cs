using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaMiaDev.ViewModels.SplitViewPane;

namespace AvaloniaMiaDev.Views;

public partial class EyeCalibrationView : UserControl
{
    public EyeCalibrationView()
    {
        InitializeComponent();
    }

    private void DecrementOrder(object sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as EyeCalibrationViewModel;
        var button = sender as Button;
        var index = int.Parse(button!.Name![^1].ToString());
        var previousIndex = viewModel!.TrackingAlgorithms[index].Order;
        var desiredIndex = previousIndex--;
        viewModel!.MoveModules(previousIndex, desiredIndex);
    }

    private void IncrementOrder(object sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as EyeCalibrationViewModel;
        var button = sender as Button;
        var index = int.Parse(button!.Name![^1].ToString());
        var previousIndex = viewModel!.TrackingAlgorithms[index].Order;
        var desiredIndex = previousIndex++;
        viewModel!.MoveModules(previousIndex, desiredIndex);
    }
}

