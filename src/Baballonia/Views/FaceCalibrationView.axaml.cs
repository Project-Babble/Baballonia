using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Baballonia.Models;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.Views;

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

