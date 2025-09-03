using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Baballonia.Helpers;
using Baballonia.ViewModels.SplitViewPane;

namespace Baballonia.Views;

public partial class HomePageView : UserControl
{
    private bool _isLayoutUpdating;

    public HomePageView()
    {
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
        Loaded += async (_, _) =>
        {
            if (DataContext is not HomePageViewModel vm) return;
            await vm.camerasInitialized.Task;

            SetupCropEvents(vm.LeftCamera, LeftMouthWindow);
            SetupCropEvents(vm.RightCamera, RightMouthWindow);
            SetupCropEvents(vm.FaceCamera, FaceWindow);
            FaceAddressEntry_OnTextChanged(null, null!);

            vm.SelectedCalibrationText = "Eye Calibration";
        };
    }

    private void SetupCropEvents(HomePageViewModel.CameraControllerModel model, Image image)
    {
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
        };
    }

    private void EyeAddressEntry_OnTextChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e is null) return; // Skip DeviceEnumerator calls
        if (DataContext is not HomePageViewModel vm || vm.FaceCamera == null) return;

        if (vm.LeftCamera.DisplayAddress.Length == 0)
        {
            LeftAddressHint.Text = "You must enter cameras for both eyes before using eye tracking!";
            vm.LeftCamera.HintEnabled = true;
            vm.LeftCamera.InferEnabled = false;
        }

        if (vm.RightCamera.DisplayAddress.Length == 0)
        {
            RightAddressHint.Text = "You must enter cameras for both eyes before using eye tracking!";
            vm.RightCamera.HintEnabled = true;
            vm.RightCamera.InferEnabled = false;
        }

        if (vm.LeftCamera.DisplayAddress.Length > 0 && vm.RightCamera.DisplayAddress.Length > 0)
        {
            vm.LeftCamera.HintEnabled = false;
            vm.RightCamera.HintEnabled = false;
            vm.LeftCamera.InferEnabled = true;
            vm.RightCamera.InferEnabled = true;
        }
    }

    private void FaceAddressEntry_OnTextChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.DataContext is not HomePageViewModel vm) return;

        if (vm.FaceCamera == null) return;
        if (!string.IsNullOrEmpty(vm.FaceCamera.DisplayAddress))
        {
            vm.FaceCamera.InferEnabled = vm.FaceCamera.DisplayAddress.Length > 0;
        }
    }

    private void OnCalibrationMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && DataContext is HomePageViewModel vm)
        {
            vm.SelectedCalibrationText = menuItem.Header?.ToString() ?? "";
        }
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

    private async void RefreshLeftEyeConnectedDevices(object? sender, CancelEventArgs e)
    {
        if (DataContext is not HomePageViewModel vm) return;

        var cameras = await App.DeviceEnumerator.UpdateCameras();
        var cameraNames = cameras.Keys.ToArray();

        vm.LeftCamera.UpdateCameraDropDown(cameraNames);
    }

    private async void RefreshRightEyeDevices(object? sender, CancelEventArgs e)
    {
        if (DataContext is not HomePageViewModel vm) return;

        var cameras = await App.DeviceEnumerator.UpdateCameras();
        var cameraNames = cameras.Keys.ToArray();

        vm.RightCamera.UpdateCameraDropDown(cameraNames);
    }

    private async void RefreshConnectedFaceDevices(object? sender, CancelEventArgs e)
    {
        if (DataContext is not HomePageViewModel vm) return;

        var cameras = await App.DeviceEnumerator.UpdateCameras();
        var cameraNames = cameras.Keys.ToArray();

        vm.FaceCamera.UpdateCameraDropDown(cameraNames);
    }
}
