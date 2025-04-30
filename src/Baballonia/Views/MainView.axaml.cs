using System;
using Avalonia;
using Avalonia.Controls;

namespace Baballonia.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        SetupViewContainer();
    }

    private void SetupViewContainer()
    {
        if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid()) // mobile
        {
            ApplicationSidePanelLogo.IsVisible = true;
            ViewContainer.CornerRadius = new CornerRadius(0, 0, 0, 0);
        }
        else
        {
            ApplicationSidePanelLogo.IsVisible = false;

            // We will just default to the existing xaml to make it more straight-forward to adjust later.
            //ViewContainer.CornerRadius = new CornerRadius(16, 0, 0, 0);
        }
    }
}
