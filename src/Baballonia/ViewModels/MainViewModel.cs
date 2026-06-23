using Avalonia.Controls;
using Baballonia.Assets;
using Baballonia.Contracts;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Baballonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly DropOverlayService _dropOverlayService;

    public MainViewModel(IMessenger messenger)
    {
        Items = Utils.IsSupportedDesktopOS ?
            new ObservableCollection<ListItemTemplate>(_desktopTemplates) :
            new ObservableCollection<ListItemTemplate>(_mobileTemplates);

        SelectedListItem = Items.First(vm => vm.ModelType == typeof(HomePageViewModel));

        _dropOverlayService = Ioc.Default.GetService<DropOverlayService>()!;
        _dropOverlayService.ShowOverlayChanged += SetOverlay;

        // The Debug / Performance page is an advanced, opt-in entry: hidden by default and revealed
        // via Settings > Advanced > "Show Debug menu". Seed from the saved setting, then stay in sync
        // with the toggle live via the messenger.
        if (Utils.IsSupportedDesktopOS)
        {
            var showDebug = Ioc.Default.GetService<ILocalSettingsService>()?
                .ReadSetting<bool>("AppSettings_ShowDebugMenu") ?? false;
            SetDebugMenuVisible(showDebug);

            messenger.Register<MainViewModel, ShowDebugMenuChangedMessage>(this,
                (recipient, message) => recipient.SetDebugMenuVisible(message.Value));
        }
    }

    private static readonly ListItemTemplate DebugMenuItem =
        new(typeof(DebugViewModel), "WindowDevToolsRegular", "Debug");

    private void SetDebugMenuVisible(bool visible)
    {
        var existing = Items.FirstOrDefault(i => i.ModelType == typeof(DebugViewModel));
        if (visible)
        {
            if (existing is null)
                Items.Add(DebugMenuItem);
        }
        else if (existing is not null)
        {
            // Don't strand the selection on a page we're about to remove.
            if (SelectedListItem == existing)
                SelectedListItem = Items.First(i => i.ModelType == typeof(HomePageViewModel));
            Items.Remove(existing);
        }
    }

    private void SetOverlay(bool show)
    {
        IsDropOverlayVisible = show;
    }

    private readonly List<ListItemTemplate> _desktopTemplates =
    [
        new(typeof(HomePageViewModel), "HomeRegular", Resources.Home_Title_Header), // Home
        new(typeof(CalibrationViewModel), "EditRegular", Resources.Calibration_Title_Header), // Calibration
        new(typeof(FirmwareViewModel), "DeveloperBoardRegular", Resources.Firmware_Title_Header), // Firmware
        new(typeof(VrcViewModel), "CommentRegular", "VRChat"), // VRChat. No translation :P
        new(typeof(OutputPageViewModel), "TextFirstLineRegular", Resources.Output_Title_Header), // Output
        new(typeof(AppSettingsViewModel), "SettingsRegular", Resources.Settings_Title_Header), // Settings
        new(typeof(AboutPageViewModel), "InfoRegular", Resources.About_Title_Header), // About
    ];

    private readonly List<ListItemTemplate> _mobileTemplates =
    [
        new(typeof(HomePageViewModel), "HomeRegular", Resources.Home_Title_Header), // Home
        new(typeof(CalibrationViewModel), "EditRegular", Resources.Calibration_Title_Header), // Calibration
        new(typeof(OutputPageViewModel), "TextFirstLineRegular", Resources.Output_Title_Header), // Output
        new(typeof(AppSettingsViewModel), "SettingsRegular", Resources.Settings_Title_Header), // Settings
        new(typeof(AboutPageViewModel), "InfoRegular", Resources.About_Title_Header), // About
    ];

    public MainViewModel() : this(new WeakReferenceMessenger()) { }

    [ObservableProperty]
    private bool _isPaneOpen;
    [ObservableProperty]
    private bool _isDropOverlayVisible;

    [ObservableProperty] private ViewModelBase _currentPage;

    [ObservableProperty]
    private ListItemTemplate? _selectedListItem;

    partial void OnSelectedListItemChanged(ListItemTemplate? value)
    {
        if (value is null) return;

        var vm = Design.IsDesignMode
            ? Activator.CreateInstance(value.ModelType)
            : CreateInstance(value.ModelType); // Manual creation

        if (vm is not ViewModelBase vmb) return;

        var tmp = CurrentPage;
        CurrentPage = vmb;

        if (tmp is IDisposable disposable)
            disposable.Dispose();
    }
    private object CreateInstance(Type type)
    {
        // Manually resolve dependencies without container tracking
        var constructors = type.GetConstructors();
        var constructor = constructors.First();
        var parameters = constructor.GetParameters()
            .Select(p => Ioc.Default.GetService(p.ParameterType))
            .ToArray();
        return Activator.CreateInstance(type, parameters)!;
    }

    public ObservableCollection<ListItemTemplate> Items { get; }

    [RelayCommand]
    private void TriggerPane()
    {
        IsPaneOpen = !IsPaneOpen;
    }

    public void DetachedFromVisualTree()
    {
        _dropOverlayService.Hide();
        _dropOverlayService.ShowOverlayChanged -= SetOverlay;
    }
}
