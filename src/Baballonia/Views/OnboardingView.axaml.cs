using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Baballonia.Services;
using Baballonia.Contracts;
using Baballonia.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Baballonia.Views;

public partial class OnboardingView : UserControl
{
    private readonly OnboardingViewModel _viewModel;

    public event EventHandler OnboardingCompleted;

    private static bool _showOnStartup;

    public OnboardingView()
    {
        InitializeComponent();

        _viewModel = Ioc.Default.GetRequiredService<OnboardingViewModel>();

        DataContext = _viewModel;

        _viewModel.OnboardingCompleted += (_, _) => OnboardingCompleted?.Invoke(this, EventArgs.Empty);

        // Initialize the view model asynchronously
        _viewModel.Initialize();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static void ShowIfNeeded(Window parent)
    {
        var localSettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _showOnStartup = localSettingsService.ReadSetting<bool>("ShowOnboardingOnStartup");

        if (_showOnStartup)
        {
            ShowOnboarding(parent);
        }
    }

    public static void ShowOnboarding(Window parent)
    {
        var overlay = new OnboardingView();

        // Create a simple host window for the overlay
        var overlayWindow = new Window
        {
            Content = overlay,
            Width = parent.Width,
            Height = parent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None,
            Background = Avalonia.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            CanResize = false,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome,
            ExtendClientAreaTitleBarHeightHint = -1
        };

        overlay.OnboardingCompleted += (_, _) => overlayWindow.Close();

        overlayWindow.ShowDialog(parent);
    }

    private void OnPreviousRequested(object? sender, RoutedEventArgs e)
    {
        _viewModel.GoToPrevious();
    }
}
