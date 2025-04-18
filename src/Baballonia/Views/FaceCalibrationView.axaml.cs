using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaloniaMiaDev.Models;
using AvaloniaMiaDev.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace AvaloniaMiaDev.Views;

public partial class FaceCalibrationView : UserControl
{
    private FaceCalibrationViewModel _viewModel;

    public FaceCalibrationView()
    {
        InitializeComponent();
        _viewModel = Ioc.Default.GetService<FaceCalibrationViewModel>()!;
    }

    private void ResetMin(object? sender, RoutedEventArgs e)
    {
        _viewModel.ResetCalibrationValues(FaceCalibrationViewModel.Selection.Min);
    }

    private void ResetMax(object? sender, RoutedEventArgs e)
    {
        _viewModel.ResetCalibrationValues(FaceCalibrationViewModel.Selection.Max);
    }
}

