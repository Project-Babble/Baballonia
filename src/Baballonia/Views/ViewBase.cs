using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Baballonia.Views;

public partial class ViewBase : UserControl
{
    [RelayCommand]
    public void OpenUrlInBrowser(string url)
    {
        Utils.OpenUrl(url);
    }
}
