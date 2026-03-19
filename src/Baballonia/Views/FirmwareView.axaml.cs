using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Baballonia.ViewModels.SplitViewPane;
using System;

namespace Baballonia.Views;

public partial class FirmwareView : ViewBase
{
    public static FilePickerFileType BinAll { get; } = new("Firmware")
    {
        Patterns = ["*.bin"],
    };

    public FirmwareView()
    {
        InitializeComponent();
        WifiNameAutoComplete.MinimumPrefixLength = 0;
        WifiNameAutoComplete.MinimumPopulateDelay = TimeSpan.Zero;
    }

    private async void CustomFirmwareLoad(object? sender, RoutedEventArgs e)
    {
        var topLevelStorageProvider = TopLevel.GetTopLevel(this)!.StorageProvider;
        var file = await topLevelStorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Firmware .bin",
            AllowMultiple = false,
            FileTypeFilter = [BinAll]
        })!;

        if (file.Count == 0) return;
        if (DataContext is not FirmwareViewModel vm) return;

        if (vm.AvailableFirmwareTypes.Contains(file[0].Name)) return;
        vm.AvailableFirmwareTypes.Add(file[0].Name);
        vm.SelectedFirmwareIndex = vm.AvailableFirmwareTypes.IndexOf(file[0].Name);
        vm.CustomFirmwarePath = file[0].Path.AbsolutePath;
    }
}
