using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.Helpers;
using AvaloniaMiaDev.Models;
using AvaloniaMiaDev.Services;
using AvaloniaMiaDev.Services.Inference;
using AvaloniaMiaDev.Services.Inference.Enums;
using AvaloniaMiaDev.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using Path = System.IO.Path;

namespace AvaloniaMiaDev.Views;

public partial class HomePageView : UserControl
{
    public static readonly StyledProperty<bool> IsLeftTrackingModeProperty =
        AvaloniaProperty.Register<HomePageView, bool>(nameof(IsLeftTrackingMode));

    public static readonly StyledProperty<bool> IsRightTrackingModeProperty =
        AvaloniaProperty.Register<HomePageView, bool>(nameof(IsRightTrackingMode));

    public static readonly StyledProperty<bool> IsFaceTrackingModeProperty =
        AvaloniaProperty.Register<HomePageView, bool>(nameof(IsFaceTrackingMode));

    public bool IsLeftTrackingMode
    {
        get => GetValue(IsLeftTrackingModeProperty);
        set => SetValue(IsLeftTrackingModeProperty, value);
    }

    public bool IsRightTrackingMode
    {
        get => GetValue(IsRightTrackingModeProperty);
        set => SetValue(IsRightTrackingModeProperty, value);
    }

    public bool IsFaceTrackingMode
    {
        get => GetValue(IsFaceTrackingModeProperty);
        set => SetValue(IsFaceTrackingModeProperty, value);
    }

    private CameraController LeftCameraController { get; }
    private CameraController RightCameraController { get; }
    private CameraController FaceCameraController { get; }

    private readonly IInferenceService _inferenceService;
    private readonly IVRService _vrService;
    private readonly HomePageViewModel _viewModel;
    private readonly ILocalSettingsService _localSettingsService;

    private bool _isVisible;

    public HomePageView()
    {
        InitializeComponent();
        Loaded += CamView_OnLoaded;
        Unloaded += CamView_Unloaded;

        _viewModel = Ioc.Default.GetRequiredService<HomePageViewModel>()!;
        _localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>()!;
        _inferenceService = Ioc.Default.GetService<IInferenceService>()!;
        _vrService = Ioc.Default.GetService<IVRService>()!;
        _localSettingsService.Load(this);

        try
        {
            var cameraEntries = DeviceEnumerator.ListCameraNames();
            LeftCameraAddressEntry.ItemsSource = cameraEntries;
            RightCameraAddressEntry.ItemsSource = cameraEntries;
            FaceCameraAddressEntry.ItemsSource = cameraEntries;
        }
        catch (Exception)
        {
            // Insufficient perms, ignore
        }

        // Initialize camera controllers
        LeftCameraController = new CameraController(
            this,
            _localSettingsService,
            _inferenceService,
            Camera.Left,
            LeftRectangleWindow,
            LeftSelectEntireFrame,
            LeftViewBox,
            LeftMouthWindow,
            LeftCanvasWindow,
            "EyeHome_LeftCameraIndex",
            "EyeHome_LeftCameraROIX",
            "EyeHome_LeftCameraROIY",
            "EyeHome_LeftCameraROIWidth",
            "EyeHome_LeftCameraROIHeight",
            "EyeHome_LeftEyeRotation",
            "EyeHome_FlipLeftEyeXAxis",
            "EyeHome_FlipLeftEyeYAxis",
            IsLeftTrackingModeProperty);

        RightCameraController = new CameraController(
            this,
            _localSettingsService,
            _inferenceService,
            Camera.Right,
            RightRectangleWindow,
            RightSelectEntireFrame,
            RightViewBox,
            RightMouthWindow,
            RightCanvasWindow,
            "EyeHome_RightCameraIndex",
            "EyeHome_RightCameraROIX",
            "EyeHome_RightCameraROIY",
            "EyeHome_RightCameraROIWidth",
            "EyeHome_RightCameraROIHeight",
            "EyeHome_RightEyeRotation",
            "EyeHome_FlipRightEyeXAxis",
            "EyeHome_FlipRightEyeYAxis",
            IsRightTrackingModeProperty);

        FaceCameraController = new CameraController(
            this,
            _localSettingsService,
            _inferenceService,
            Camera.Face,
            FaceRectangleWindow,
            FaceSelectEntireFrame,
            FaceViewBox,
            FaceMouthWindow,
            FaceCanvasWindow,
            "EyeHome_FaceCameraIndex",
            "Face_CameraROIX",
            "Face_CameraROIY",
            "Face_CameraROIWidth",
            "Face_CameraROIHeight",
            "Face_Rotation",
            "Face_FlipXAxis",
            "Face_FlipYAxis",
            IsFaceTrackingModeProperty);

        var parameterSenderService = Ioc.Default.GetService<ParameterSenderService>()!;
        parameterSenderService.RegisterLeftCameraController(LeftCameraController!);
        parameterSenderService.RegisterRightCameraController(RightCameraController!);
        parameterSenderService.RegisterFaceCameraController(FaceCameraController!);

        StartImageUpdates();

        PropertyChanged += (_, _) => { _localSettingsService.Save(this); };
    }

    private void CamView_OnLoaded(object? sender, RoutedEventArgs e)
    {
        _isVisible = true;
        UpdateAddressHint(LeftCameraAddressEntry, LeftAddressHint);
        UpdateAddressHint(RightCameraAddressEntry, RightAddressHint);
        UpdateAddressHint(FaceCameraAddressEntry, FaceAddressHint);
    }

    private void CamView_Unloaded(object? sender, RoutedEventArgs e)
    {
        LeftCameraController.StopMjpegStreaming();
        RightCameraController.StopMjpegStreaming();
        _isVisible = false;
    }

    private void StartImageUpdates()
    {
        DispatcherTimer drawTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        drawTimer.Tick += async (s, e) =>
        {
            await LeftCameraController.UpdateImage(_isVisible);
            await RightCameraController.UpdateImage(_isVisible);
            await FaceCameraController.UpdateImage(_isVisible);

            _viewModel.LeftEyeBitmap = LeftCameraController.Bitmap;
            _viewModel.RightEyeBitmap = RightCameraController.Bitmap;
            _viewModel.FaceBitmap = FaceCameraController.Bitmap;
        };
        drawTimer.Start();
    }

    // Event handlers for left camera
    public void LeftCameraStart(object? sender, RoutedEventArgs e)
    {
        LeftCameraController.StopCamera();
        LeftCameraController.StartCamera(LeftCameraAddressEntry.Text!);
    }

    private void LeftCameraStopped(object? sender, RoutedEventArgs e)
    {
        LeftCameraController.StopCamera();
        LeftCameraController.StopMjpegStreaming();
    }

    public void LeftOnTrackingModeClicked(object sender, RoutedEventArgs args)
    {
        LeftCameraController.SetTrackingMode();
    }

    public void LeftOnCroppingModeClicked(object sender, RoutedEventArgs args)
    {
        LeftCameraController.SetCroppingMode();
    }

    public async void LeftSelectEntireFrameClicked(object sender, RoutedEventArgs args)
    {
        await LeftCameraController.SelectEntireFrame();
    }

    // Event handlers for right camera
    public void RightCameraStart(object? sender, RoutedEventArgs e)
    {
        RightCameraController.StopCamera();
        RightCameraController.StartCamera(RightCameraAddressEntry.Text!);
    }

    public void RightCameraStopped(object? sender, RoutedEventArgs e)
    {
        RightCameraController.StopCamera();
        RightCameraController.StopMjpegStreaming();
    }

    public void RightOnTrackingModeClicked(object sender, RoutedEventArgs args)
    {
        RightCameraController.SetTrackingMode();
    }

    public void RightOnCroppingModeClicked(object sender, RoutedEventArgs args)
    {
        RightCameraController.SetCroppingMode();
    }

    public async void RightSelectEntireFrameClicked(object sender, RoutedEventArgs args)
    {
        await RightCameraController.SelectEntireFrame();
    }

    // Event handlers for face camera
    public void FaceCameraStart(object? sender, RoutedEventArgs e)
    {
        FaceCameraController.StopCamera();
        FaceCameraController.StartCamera(FaceCameraAddressEntry.Text!);
    }

    public void FaceCameraStopped(object? sender, RoutedEventArgs e)
    {
        FaceCameraController.StopCamera();
    }

    public void FaceOnTrackingModeClicked(object sender, RoutedEventArgs args)
    {
        FaceCameraController.SetTrackingMode();
    }

    public void FaceOnCroppingModeClicked(object sender, RoutedEventArgs args)
    {
        FaceCameraController.SetCroppingMode();
    }

    public async void FaceSelectEntireFrameClicked(object sender, RoutedEventArgs args)
    {
        await FaceCameraController.SelectEntireFrame();
    }

    private async void OnVRCalibrationRequested(object? sender, RoutedEventArgs e)
    {
        // Gatekeep the SteamVR Overlay
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux())) return;

        const int leftPort = 8080;
        const int rightPort = 8081;
        var modelPath = Directory.GetCurrentDirectory(); // This should ideally point to a more persistent dir at some point

        var model = new VrCalibration
        {
            ModelSavePath = modelPath,
            CalibrationInstructions = "2",
            FOV = 1f,
            LeftEyeMjpegSource = $"http://localhost:{leftPort}/mjpeg",
            RightEyeMjpegSource = $"http://localhost:{rightPort}/mjpeg",
        };

        // Now for the IPC. Spool up our MJPEG streams
        LeftCameraController.StartMjpegStreaming(leftPort);
        RightCameraController.StartMjpegStreaming(rightPort);

        // First tell the subprocess to accept our streams, then start calibration
        await _vrService.StartCamerasAsync(model);
        await _vrService.StartCalibrationAsync(model);

        // Wait for the process to exit
        var loop = true;
        while (loop)
        {
            var status = await _vrService.GetStatusAsync();
            if (status.IsTrained)
            {
                loop = false;
            }

            await Task.Delay(1000);
        }

        // Cleanup
        LeftCameraController.StopMjpegStreaming();
        RightCameraController.StopMjpegStreaming();

        // Save the location of the model so when we boot up the app it auto-loads
        var modelName = Path.Combine(modelPath, VrCalibration.ModelName);
        await _localSettingsService.SaveSettingAsync("EyeHome_EyeModel", modelName);

        // Instruct the inference service to load the new model
        var minCutoff = await _localSettingsService.ReadSettingAsync<float>("AppSettings_OneEuroMinFreqCutoff");
        var speedCoeff = await _localSettingsService.ReadSettingAsync<float>("AppSettings_OneEuroSpeedCutoff");
        SessionOptions sessionOptions = _inferenceService.SetupSessionOptions();
        await _inferenceService.ConfigurePlatformSpecificGpu(sessionOptions);

        // Finally, close any open eye cameras. The inference service will spin these up
        LeftCameraController.StopCamera();
        RightCameraController.StopCamera();
        _inferenceService.SetupInference(modelName, Camera.Left, minCutoff, speedCoeff, sessionOptions);
        _inferenceService.ConfigurePlatformConnectors(Camera.Left, _viewModel.LeftCameraAddress);
        _inferenceService.SetupInference(modelName, Camera.Right, minCutoff, speedCoeff, sessionOptions);
        _inferenceService.ConfigurePlatformConnectors(Camera.Right, _viewModel.RightCameraAddress);
    }

    private void CameraAddressEntry_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not AutoCompleteBox entry) return;

        // Determine which hint TextBlock corresponds to the sender
        TextBlock? hintBlock = entry.Name switch
        {
            nameof(LeftCameraAddressEntry) => LeftAddressHint,
            nameof(RightCameraAddressEntry) => RightAddressHint,
            nameof(FaceCameraAddressEntry) => FaceAddressHint,
            _ => null
        };

        if (hintBlock != null)
        {
            UpdateAddressHint(entry, hintBlock);
        }
    }

    private void UpdateAddressHint(AutoCompleteBox entry, TextBlock hint)
    {
        string? address = entry.Text;
        bool showHint = false;

        if (!string.IsNullOrWhiteSpace(address))
        {
            // Basic check: Does it contain ".local" but not "http://"?
            if (address.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
                !address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                showHint = true;
                hint.Text = "Address might need to start with 'http://' (e.g., http://openiristracker.local/)";
            }
        }

        hint.IsVisible = showHint;
    }
}
