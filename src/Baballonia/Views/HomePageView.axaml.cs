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

    // Add these back if we need hint text again
    /*private void EyeAddressEntry_OnTextChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e is null) return; // Skip DeviceEnumerator calls
        if (DataContext is not HomePageViewModel vm || vm.FaceCamera == null) return;

        if (vm.LeftCamera.DisplayAddress.Length == 0)
        {
            LeftAddressHint.Text = "You must enter cameras for both eyes before using eye tracking!";
            vm.LeftCamera.HintEnabled = true;
        }

        if (vm.RightCamera.DisplayAddress.Length == 0)
        {
            RightAddressHint.Text = "You must enter cameras for both eyes before using eye tracking!";
            vm.RightCamera.HintEnabled = true;
        }

        if (vm.LeftCamera.DisplayAddress.Length > 0 && vm.RightCamera.DisplayAddress.Length > 0)
        {
            vm.LeftCamera.HintEnabled = false;
            vm.RightCamera.HintEnabled = false;
        }
    }*/

    // Add these back if we need hint text again
    /*private void FaceAddressEntry_OnTextChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.DataContext is not HomePageViewModel vm) return;

        if (vm.FaceCamera == null) return;
        if (!string.IsNullOrEmpty(vm.FaceCamera.DisplayAddress))
        {
            vm.FaceCamera.HintEnabled = vm.FaceCamera.DisplayAddress.Length > 0;
        }
    }*/

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
        if (DataContext is not HomePageViewModel vm) return;
        vm.LeftCamera.UpdateCameraDropDown();
    }

    private void RefreshRightEyeDevices(object? sender, CancelEventArgs e)
    {
        if (DataContext is not HomePageViewModel vm) return;
        vm.RightCamera.UpdateCameraDropDown();
    }

    private void RefreshConnectedFaceDevices(object? sender, CancelEventArgs e)
    {
        if (DataContext is not HomePageViewModel vm) return;
        vm.FaceCamera.UpdateCameraDropDown();
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

        vm.LocalSettingsService.SaveSetting("EyeHome_EyeModel", file[0].Path.AbsolutePath);
        var eye = await vm.ProcessingLoopService.LoadEyeInferenceAsync();
        vm.ProcessingLoopService.EyesProcessingPipeline.InferenceService = eye;

        LoadEyeModelText.Text = file[0].Name;
        LoadEyeModelText.Foreground = new SolidColorBrush(Colors.Green);
        await Task.Delay(3000);
        LoadEyeModelText.Text = Baballonia.Assets.Resources.Home_Eye_Load_Model;
        LoadEyeModelText.Foreground = new SolidColorBrush(vm.GetBaseHighColor());
    }
}
