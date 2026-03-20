using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Linq;

namespace Baballonia;

// NativeMenu helper class with a few hacks to handle locale
public partial class App : Application
{
    private TrayIcon _appTrayTooltip;
    private NativeMenuItem _trayShowHideItem;
    private NativeMenuItem _trayExitItem;

    private void InitTray()
    {
        var icons = TrayIcon.GetIcons(this);
        _appTrayTooltip = icons?.OfType<TrayIcon>().First()!;

        // If we ever reorder/add a new native menu item, this will break. Too bad!
        // Layout: items[0]: Header, items[1]: Seperator, items[2]: Exit
        var items = _appTrayTooltip?.Menu?.Items.OfType<NativeMenuItem>().ToArray(); // Icon
        _trayShowHideItem = items?[0]!;                                              // Show/Hide
        _trayExitItem = items?[2]!;                                                  // Exit

        RefreshTrayStrings();
    }

    private void RefreshTrayStrings()
    {
        _appTrayTooltip.ToolTipText = Assets.Resources.Tray_Tooltip;
        _trayShowHideItem.Header = Assets.Resources.Tray_Hide;
        _trayExitItem.Header = Assets.Resources.Tray_Exit;
    }

    // Intellisense says this has 0 references - this is referenced by App.axaml
    private void OnTrayShowHideClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        if (desktop.MainWindow!.IsVisible)
        {
            desktop.MainWindow.Hide();
            if (sender is NativeMenuItem showHide)
            {
                showHide.Header = Assets.Resources.Tray_Show;
            }
        }
        else
        {
            desktop.MainWindow.Show();
            if (sender is NativeMenuItem showHide)
            {
                showHide.Header = Assets.Resources.Tray_Hide;
            }
        }
    }

    // Intellisense says this has 0 references - this is referenced by App.axaml
    private void OnTrayShutdownClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
