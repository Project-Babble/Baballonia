using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Baballonia.Views;

public partial class CalibrationView : ViewBase
{
    public CalibrationView()
    {
        InitializeComponent();

        if (!Utils.IsSupportedDesktopOS)
        {
            SizeChanged += (_, _) =>
            {
                if (this.GetVisualRoot() is not Window window) return;

                var desktopLayout = this.FindControl<StackPanel>("ResetDesktopStackPanel");
                var mobileLayout = this.FindControl<StackPanel>("ResetMobileStackPanel");
                if (window.ClientSize.Width < Utils.MobileWidth)
                {
                    desktopLayout!.IsVisible = false;
                    mobileLayout!.IsVisible = true;
                }
                else
                {
                    desktopLayout!.IsVisible = true;
                    mobileLayout!.IsVisible = false;
                }
            };
        }
    }
}

