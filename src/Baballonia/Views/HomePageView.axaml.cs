using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Baballonia.Calibration;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Models;
using Baballonia.Services;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Enums;
using Baballonia.ViewModels.SplitViewPane;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using Path = System.IO.Path;

namespace Baballonia.Views;

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

    private readonly IEyeInferenceService _eyeInferenceService;
    private readonly IFaceInferenceService _faceInferenceService;
    private readonly IVrService _vrService;
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
        _eyeInferenceService = Ioc.Default.GetService<IEyeInferenceService>()!;
        _faceInferenceService = Ioc.Default.GetService<IFaceInferenceService>()!;
        _vrService = Ioc.Default.GetService<IVrService>()!;
        _localSettingsService.Load(this);

        try
        {
            var cameraDevices = DeviceEnumerator.ListCameras();

            var cameraEntries = cameraDevices.Keys;
            LeftCameraAddressEntry.ItemsSource = cameraEntries;
            RightCameraAddressEntry.ItemsSource = cameraEntries;
            FaceCameraAddressEntry.ItemsSource = cameraEntries;

            // Set MinimumPrefixLength to 0 to show all items even when no text is entered
            // Set MinimumPopulateDelay to 0 to show the dropdown immediately
            LeftCameraAddressEntry.MinimumPrefixLength = 0;
            LeftCameraAddressEntry.MinimumPopulateDelay = TimeSpan.Zero;

            RightCameraAddressEntry.MinimumPrefixLength = 0;
            RightCameraAddressEntry.MinimumPopulateDelay = TimeSpan.Zero;

            FaceCameraAddressEntry.MinimumPrefixLength = 0;
            FaceCameraAddressEntry.MinimumPopulateDelay = TimeSpan.Zero;
        }
        catch (Exception)
        {
            // Insufficient perms, ignore
        }

        // Initialize camera controllers
        LeftCameraController = new CameraController(
            this,
            _localSettingsService,
            _eyeInferenceService,
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
            _eyeInferenceService,
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
            _faceInferenceService,
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
        string selectedFriendlyName = LeftCameraAddressEntry.Text!;
        string cameraAddress = selectedFriendlyName;

        // If the friendly name exists in our dictionary, use the corresponding device ID
        if (DeviceEnumerator.Cameras.TryGetValue(selectedFriendlyName, out var deviceId))
        {
            cameraAddress = deviceId;
        }

        LeftCameraController.StartCamera(cameraAddress);
    }

    public void StopLeftCamera(object? sender, RoutedEventArgs e)
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
        string selectedFriendlyName = RightCameraAddressEntry.Text!;
        string cameraAddress = selectedFriendlyName;

        // If the friendly name exists in our dictionary, use the corresponding device ID
        if (DeviceEnumerator.Cameras.TryGetValue(selectedFriendlyName, out var deviceId))
        {
            cameraAddress = deviceId;
        }

        RightCameraController.StartCamera(cameraAddress);
    }


    public void StopRightCamera(object? sender, RoutedEventArgs e)
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
        string selectedFriendlyName = FaceCameraAddressEntry.Text!;
        string cameraAddress = selectedFriendlyName;

        // If the friendly name exists in our dictionary, use the corresponding device ID
        if (DeviceEnumerator.Cameras.TryGetValue(selectedFriendlyName, out var deviceId))
        {
            cameraAddress = deviceId;
        }

        FaceCameraController.StartCamera(cameraAddress);
    }

    public void StopFaceCamera(object? sender, RoutedEventArgs e)
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
        var modelPath = Directory.GetCurrentDirectory();

        var model = new VrCalibration
        {
            ModelSavePath = modelPath,
            CalibrationInstructions = CalibrationRoutine.HorizontalSweep,
            FOV = 1f,
            LeftEyeMjpegSource = $"http://localhost:{leftPort}/mjpeg",
            RightEyeMjpegSource = $"http://localhost:{rightPort}/mjpeg",
        };

        // Now for the IPC. Spool up our MJPEG streams
        LeftCameraController.StartMjpegStreaming(leftPort);
        RightCameraController.StartMjpegStreaming(rightPort);

        // First tell the subprocess to accept our streams, then start calibration
        await _vrService.StartOverlay();
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

        // Stop the MJPEG streams, we don't need them anymore
        LeftCameraController.StopMjpegStreaming();
        RightCameraController.StopMjpegStreaming();
        _vrService.StopOverlay();

        // Sanity check: If the trainer hasn't started already, do it ourselves.
        // This blocks until training is complete
        await _vrService.StartTrainer(arguments: ["capture.bin"]);
        _vrService.StopTrainer();

        // Save the location of the model so when we boot up the app it autoloads
        var modelName = "tuned_model.onnx";
        await _localSettingsService.SaveSettingAsync("EyeHome_EyeModel", modelName);

        // Cleanup any leftover capture.bin files
        DeleteCaptureFiles(modelPath);

        // Instruct the inference service to load the new model
        var minCutoff = await _localSettingsService.ReadSettingAsync<float>("AppSettings_OneEuroMinFreqCutoff");
        var speedCoeff = await _localSettingsService.ReadSettingAsync<float>("AppSettings_OneEuroSpeedCutoff");
        SessionOptions sessionOptions = _eyeInferenceService.SetupSessionOptions();
        await _eyeInferenceService.ConfigurePlatformSpecificGpu(sessionOptions);

        // Finally, close any open eye cameras. The inference service will spin these up
        LeftCameraController.StopCamera();
        RightCameraController.StopCamera();
        _eyeInferenceService.SetupInference(modelName, Camera.Left, minCutoff, speedCoeff, sessionOptions);
        _eyeInferenceService.ConfigurePlatformConnectors(Camera.Left, _viewModel.LeftCameraAddress);
        _eyeInferenceService.SetupInference(modelName, Camera.Right, minCutoff, speedCoeff, sessionOptions);
        _eyeInferenceService.ConfigurePlatformConnectors(Camera.Right, _viewModel.RightCameraAddress);
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

    public static void DeleteCaptureFiles(string directoryPath)
    {
        // Validate directory exists
        if (!Directory.Exists(directoryPath))
            return;

        // Get all files matching the capture pattern
        string[] filesToDelete = Directory.GetFiles(directoryPath, "capture.bin");

        // Delete each file
        foreach (string file in filesToDelete)
        {
            File.Delete(file);
        }
    }
}
