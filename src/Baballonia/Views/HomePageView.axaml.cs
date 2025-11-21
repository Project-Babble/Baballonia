using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Baballonia.Assets;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.ViewModels.SplitViewPane;

namespace Baballonia.Views;

public partial class HomePageView : UserControl
{

    public static FilePickerFileType ONNXAll { get; } = new("ONNX Models")
    {
        Patterns = ["*.onnx"],
    };

    private bool _isLayoutUpdating;

    private readonly IDeviceEnumerator _deviceEnumerator;
    private readonly ILocalSettingsService _localSettings;

    public HomePageView(IDeviceEnumerator deviceEnumerator, ILocalSettingsService localSettings)
    {
        _deviceEnumerator = deviceEnumerator;
        _localSettings = localSettings;
        InitializeComponent();

        if (Utils.IsSupportedDesktopOS)
        {
            SizeChanged += (_, _) =>
            {
                if (this.GetVisualRoot() is not Window window) return;

                var camerasGrid = this.FindControl<Grid>("CameraControlsGrid");
                var eyesGrid = this.FindControl<Grid>("EyesGrid");
                var isVertical = window.ClientSize.Width < Utils.MobileWidth;

                // Clear existing row/column definitions
                camerasGrid!.RowDefinitions.Clear();
                camerasGrid.ColumnDefinitions.Clear();

                eyesGrid!.RowDefinitions.Clear();
                eyesGrid.ColumnDefinitions.Clear();

                if (isVertical)
                {
                    // Vertical layout - one column, three rows
                    camerasGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    camerasGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    camerasGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                    eyesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    eyesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    eyesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                    // Set grid positions for all children
                    for (var i = 0; i < camerasGrid.Children.Count; i++)
                    {
                        var child = camerasGrid.Children[i];
                        Grid.SetRow(child, i);
                        Grid.SetColumn(child, 0);
                        child.Margin = new Avalonia.Thickness(0, 0, 0, 16);
                    }
                    for (var i = 0; i < eyesGrid.Children.Count; i++)
                    {
                        var child = eyesGrid.Children[i];
                        Grid.SetRow(child, i);
                        Grid.SetColumn(child, 0);
                        child.Margin = new Avalonia.Thickness(0, 0, 0, 16);
                    }
                }
                else
                {
                    // Horizontal layout - three columns, one row
                    camerasGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Parse("2*")));
                    camerasGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    camerasGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                    eyesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    eyesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    eyesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                    // Set grid positions for all children
                    for (var i = 0; i < camerasGrid.Children.Count; i++)
                    {
                        var child = camerasGrid.Children[i];
                        Grid.SetRow(child, 0);
                        Grid.SetColumn(child, i);
                        child.Margin = new Avalonia.Thickness(0, 0, i < 2 ? 12 : 0, 0);
                    }
                    for (var i = 0; i < eyesGrid.Children.Count; i++)
                    {
                        var child = eyesGrid.Children[i];
                        Grid.SetRow(child, 0);
                        Grid.SetColumn(child, i);
                        child.Margin = new Avalonia.Thickness(0, 0, i < 2 ? 12 : 0, 0);
                    }
                }
            };
        }
        Loaded += (_, _) =>
        {
            if (DataContext is not HomePageViewModel vm) return;

            SetupCropEvents(vm.LeftCamera, LeftMouthWindow);
            SetupCropEvents(vm.RightCamera, RightMouthWindow);
            SetupCropEvents(vm.FaceCamera, FaceWindow);

            vm.SelectedCalibrationTextBlock = this.Find<TextBlock>("SelectedCalibrationTextBlockColor")!;
            vm.SelectedCalibrationTextBlock.Text = Assets.Resources.Home_Eye_Calibration;
        };
    }

    private void SetupCropEvents(HomePageViewModel.CameraControllerModel model, Image image)
    {
        if (DataContext is not HomePageViewModel vm) return;

        // in theory should be cleaned up by the GC so no need to manually unsubscribe
        image.PointerPressed += (sender, e) =>
        {
            if (model.CamViewMode != CamViewMode.Cropping) return;
            var pos = e.GetPosition(image);
            model.CropManager.StartCrop(pos);
            model.OverlayRectangle = model.CropManager.CropZone.GetRect();
        };
        image.PointerMoved += (sender, e) =>
        {
            if (model.CamViewMode != CamViewMode.Cropping) return;

            var pos = e.GetPosition(image);
            model.CropManager.UpdateCrop(pos);
            model.OverlayRectangle = model.CropManager.CropZone.GetRect();
        };
        image.PointerReleased += (sender, e) =>
        {
            if (model.CamViewMode != CamViewMode.Cropping) return;

            model.CropManager.EndCrop();
            model.OnCropUpdated();
            vm.OnCropUpdated(model);
        };
    }

    private void OnCalibrationMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || DataContext is not HomePageViewModel vm) return;

        vm.SelectedCalibrationTextBlock.Text = menuItem.Header?.ToString()!;
        vm.RequestedVRCalibration = CalibrationRoutine.Map[menuItem.Name!];
    }

    private void OnExpanderCollapsed(object? sender, RoutedEventArgs e)
    {
        if (_isLayoutUpdating) return;
        _isLayoutUpdating = true;

        // Force layout update
        InvalidateArrange();
        InvalidateMeasure();

        _isLayoutUpdating = false;
    }

    private void OnExpanderExpanded(object? sender, RoutedEventArgs e)
    {
        if (_isLayoutUpdating) return;
        _isLayoutUpdating = true;

        // Force layout update
        InvalidateArrange();
        InvalidateMeasure();

        _isLayoutUpdating = false;
    }

    private void RefreshLeftEyeConnectedDevices(object? sender, CancelEventArgs e)
    {
        //if (DataContext is not HomePageViewModel vm) return;
        //vm.LeftCamera.UpdateCameraDropDown();
    }

    private void RefreshRightEyeDevices(object? sender, CancelEventArgs e)
    {
        //if (DataContext is not HomePageViewModel vm) return;
        //vm.RightCamera.UpdateCameraDropDown();
    }

    private void RefreshConnectedFaceDevices(object? sender, CancelEventArgs e)
    {
        //if (DataContext is not HomePageViewModel vm) return;
        //vm.FaceCamera.UpdateCameraDropDown();
    }

    private async void RefreshDevices(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomePageViewModel vm) return;
        RefreshDevicesText.IsEnabled = false;

        try
        {
            var cameras = _deviceEnumerator.UpdateCameras();
            var friendlyNames = cameras.Keys.ToArray();

            vm.LeftCamera.UpdateCameraDropDown(friendlyNames);
            vm.RightCamera.UpdateCameraDropDown(friendlyNames);
            vm.FaceCamera.UpdateCameraDropDown(friendlyNames);
        }
        catch (Exception exception)
        {
        }

        RefreshDevicesText.IsEnabled = true;
    }

    private async void EyeModelLoad(object? sender, RoutedEventArgs e)
    {
        var topLevelStorageProvider = TopLevel.GetTopLevel(this)!.StorageProvider;
        var suggestedStartLocation =
            await topLevelStorageProvider.TryGetFolderFromPathAsync(Utils.ModelsDirectory)!;
        var file = await topLevelStorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select ONNX Model",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation, // Falls back to desktop if Models folder hasn't been created yet
            FileTypeFilter = [ONNXAll]
        })!;

        if (file.Count == 0) return;
        if (DataContext is not HomePageViewModel vm) return;

        _localSettings.SaveSetting("EyeHome_EyeModel", file[0].Path.AbsolutePath);

        await vm.ReloadEyeInference();

        LoadEyeModelText.Text = file[0].Name;
        LoadEyeModelText.Foreground = new SolidColorBrush(Colors.Green);
        await Task.Delay(3000);
        LoadEyeModelText.Text = Baballonia.Assets.Resources.Home_Eye_Load_Model;
        LoadEyeModelText.Foreground = new SolidColorBrush(vm.GetBaseHighColor());
    }
}
