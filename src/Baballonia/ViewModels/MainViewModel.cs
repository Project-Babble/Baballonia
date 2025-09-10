using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Views;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace Baballonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly DropOverlayService _dropOverlayService;
    private readonly UpdateService _updateService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel() : this(
        Ioc.Default.GetRequiredService<UpdateService>(),
        Ioc.Default.GetService<ILogger<MainViewModel>>()!,
        Ioc.Default.GetService<ILocalSettingsService>()!)
    {
    }

    public MainViewModel(UpdateService updateService, ILogger<MainViewModel> logger,
        ILocalSettingsService localSettingsService)
    {
        _updateService = updateService;
        _logger = logger;
        _localSettingsService = localSettingsService;

        Items = Utils.IsSupportedDesktopOS
            ? new ObservableCollection<ListItemTemplate>(_desktopTemplates)
            : new ObservableCollection<ListItemTemplate>(_mobileTemplates);

        SelectedListItem = Items.First(vm => vm.ModelType == typeof(HomePageViewModel));

        _dropOverlayService = Ioc.Default.GetService<DropOverlayService>()!;
        _dropOverlayService.ShowOverlayChanged += SetOverlay;

        PromptUpdate();
    }

    private void PromptUpdate()
    {
        Task.Run(async () =>
        {
            var shouldCheck = await _localSettingsService.ReadSettingAsync<bool>("AppSettings_CheckForUpdates", false);
            if (!shouldCheck)
                return;

            var isLatest = await _updateService.IsLatest();
            Version? latestVer = null;
            if (!isLatest)
                latestVer = await _updateService.TryGetLatestVersion();

            _logger.LogInformation(isLatest
                ? "The current version is latest!"
                : $"New version {latestVer!.ToString()} available");

            await Dispatcher.UIThread.InvokeAsync(() => { ShouldPromptUpdate = true; }, DispatcherPriority.Background);
        });
    }

    [RelayCommand]
    private async Task OpenBrowserOnLatest()
    {
        await Task.Run(() => { _updateService.NavigateToLatestWebPage(); });
        await Dispatcher.UIThread.InvokeAsync(() => { ShouldPromptUpdate = false; });
    }

    [RelayCommand]
    private void CloseUpdatePrompt()
    {
        ShouldPromptUpdate = false;
    }


    private void SetOverlay(bool show)
    {
        IsDropOverlayVisible = show;
    }

    private readonly List<ListItemTemplate> _desktopTemplates =
    [
        new(typeof(HomePageViewModel), "HomeRegular", "Home"),
        new(typeof(CalibrationViewModel), "EditRegular", "Calibration"),
        new(typeof(FirmwareViewModel), "DeveloperBoardRegular", "Firmware"),
        new(typeof(VrcViewModel), "CommentRegular", "VRChat"),
        new(typeof(OutputPageViewModel), "TextFirstLineRegular", "Output"),
        new(typeof(AppSettingsViewModel), "SettingsRegular", "Settings"),
    ];

    private readonly List<ListItemTemplate> _mobileTemplates =
    [
        new(typeof(HomePageViewModel), "HomeRegular", "Home"),
        new(typeof(CalibrationViewModel), "EditRegular", "Calibration"),
        new(typeof(OutputPageViewModel), "TextFirstLineRegular", "Output"),
        new(typeof(AppSettingsViewModel), "SettingsRegular", "Settings"),
    ];

    [ObservableProperty] private bool _isPaneOpen;
    [ObservableProperty] private bool _isDropOverlayVisible;
    [ObservableProperty] private bool _shouldPromptUpdate = false;

    [ObservableProperty] private ViewModelBase _currentPage;
    [ObservableProperty] private ListItemTemplate? _selectedListItem;

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
