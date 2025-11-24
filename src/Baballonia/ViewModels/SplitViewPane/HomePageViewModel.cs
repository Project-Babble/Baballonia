using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Services;
using Baballonia.Services.events;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Buffer = System.Buffer;
using Rect = Avalonia.Rect;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class HomePageViewModel : ViewModelBase, IDisposable
{
    // This feels unorthodox but... i kinda like it?
    public partial class CameraControllerModel : ObservableObject
    {
        public string Name;
        public readonly CropManager CropManager = new();
        public CamViewMode CamViewMode = CamViewMode.Tracking;
        public readonly Camera Camera;
        [ObservableProperty] private bool _shouldAutostart = false;

        public CameraSettings CameraSettings;

        [ObservableProperty] private WriteableBitmap? _bitmap;
        [ObservableProperty] private bool _startButtonEnabled = true;
        [ObservableProperty] private bool _stopButtonEnabled = false;
        [ObservableProperty] private bool _hintEnabled = false;
        [ObservableProperty] private Rect _overlayRectangle;

        // this particular property is used as event indicator lmao
        [ObservableProperty] private bool _importantSettingsProperty;

        [ObservableProperty] private string _displayAddress;
        [ObservableProperty] private bool _flipHorizontally = false;
        [ObservableProperty] private bool _flipVertically = false;
        [ObservableProperty] private float _rotation = 0f;
        [ObservableProperty] private float _gamma = 1f;
        [ObservableProperty] private bool _isCropMode = false;
        [ObservableProperty] private bool _isCameraRunning = false;
        [ObservableProperty] private string _selectedCaptureMethod = "";
        [ObservableProperty] private bool _captureMethodVisible = false;
        public ObservableCollection<string> Suggestions { get; set; } = [];
        public ObservableCollection<string> CaptureMethods { get; set; } = [];

        private readonly ILocalSettingsService _localSettingsService;
        private readonly IPlatformConnector _platformConnector;
        private readonly IDeviceEnumerator _deviceEnumerator;


        public CameraControllerModel(ILocalSettingsService localSettingsService, IPlatformConnector platformConnector,
            IDeviceEnumerator deviceEnumerator,
            string name, string[] cameras,
            Camera camera)
        {
            _localSettingsService = localSettingsService;
            _platformConnector = platformConnector;
            _deviceEnumerator = deviceEnumerator;

            Name = name;
            Camera = camera;

            var roi = new RegionOfInterest();
            CameraSettings = new CameraSettings(camera, roi);

            Initialize(cameras);
        }

        private void Initialize(string[] cameras)
        {
            var displayAddress = _localSettingsService.ReadSetting<string>("LastOpened" + Name);
            var camSettings = _localSettingsService.ReadSetting<CameraSettings>(Name) ?? CameraSettings;
            ShouldAutostart = _localSettingsService.ReadSetting("ShouldAutostart" + Name, false);
            var preferredCapture = _localSettingsService.ReadSetting<string>("LastOpenedPreferredCapture" + Name);
            if (preferredCapture == "Default") preferredCapture = Assets.Resources.Home_Backend_Default;

            UpdateCameraDropDown(cameras);
            DisplayAddress = displayAddress;
            FlipHorizontally = camSettings.UseHorizontalFlip;
            FlipVertically = camSettings.UseVerticalFlip;
            Rotation = camSettings.RotationRadians;
            Gamma = camSettings.Gamma;
            SelectedCaptureMethod = preferredCapture;


            CropManager.SetCropZone(camSettings.Roi);
            OverlayRectangle = CropManager.CropZone.GetRect();

            CameraSettings = camSettings;
            OnCropUpdated();
        }

        partial void OnDisplayAddressChanged(string value)
        {
            // "it looks like shit" be damned, i've seen *way* too many instances of people needing to force
            // a capture backend and this stupid visual preference garbage is getting in the way of it.
            // we're going to get more support tickets from people who can't find this when they're on linux or
            // using vive face trackers.
            // if you think it looks bad, suck it, make it look better yourself, it has to be here,
            // the NEED take precedent over visual preferences.
            //var advancedEnabled = _localSettingsService.ReadSetting<bool>("AppSettings_AdvancedOptions");
            var advancedEnabled = true;
            var availableCaptureFactories = _platformConnector.GetCaptureFactories()
                .Where(factory => factory.CanConnect(value)).ToArray();

            var shouldShow = advancedEnabled && availableCaptureFactories.Length >= 2;
            CaptureMethodVisible = shouldShow;

            CaptureMethods.Clear();
            // IF YOU'RE TRYING TO ENABLE A SPECIFIC BACKEND FOR A CAPTURE, DO NOT TOUCH THIS!
            // THIS IS NOT WHERE YOU FORCE A BACKEND
            // OVERRIDE THE CANCAPTURE() METHOD INSIDE THE CAPTURE'S CAPTURE FACTORY
            if (shouldShow)
            {
                CaptureMethods.Add(Assets.Resources.Home_Backend_Default);
                foreach (var match in availableCaptureFactories)
                    CaptureMethods.Add(match.GetProviderName());

                SelectedCaptureMethod = availableCaptureFactories.First().GetProviderName();
            }
            else
            {
                SelectedCaptureMethod = "";
            }
        }

        partial void OnSelectedCaptureMethodChanged(string value)
        {
            var prev = _localSettingsService.ReadSetting<string>("LastOpenedPreferredCapture" + Name);
            if (prev != value)
                _localSettingsService.SaveSetting("LastOpenedPreferredCapture" + Name,
                    value == Assets.Resources.Home_Backend_Default ? "Default" : value);
        }

        partial void OnShouldAutostartChanged(bool value)
        {
            var prev = _localSettingsService.ReadSetting("ShouldAutostart" + Name, false);
            if (prev != value)
                _localSettingsService.SaveSetting("ShouldAutostart" + Name, value);
        }

        public void UpdateCameraDropDown()
        {
            var friendlyNames = _deviceEnumerator.UpdateCameras().Keys.ToArray();
            UpdateCameraDropDown(friendlyNames);
        }

        public void UpdateCameraDropDown(string[] cameras)
        {
            var prev = DisplayAddress;

            Suggestions.Clear();
            foreach (var key in cameras)
            {
                Suggestions.Add(key);
            }

            DisplayAddress = prev;
        }


        public void OnCropUpdated()
        {
            OverlayRectangle = CropManager.CropZone.GetRect();
            Save();
        }

        public void FaceNewImageUpdateEventHandler(FacePipelineEvents.NewFrameEvent e)
        {
            if (IsCropMode)
                FaceImageUpdateHandler(e.image);
        }

        public void FaceNewTransformedUpdateEventHandler(FacePipelineEvents.NewTransformedFrameEvent e)
        {
            if (!IsCropMode)
                FaceImageUpdateHandler(e.image);
        }

        private void FaceImageUpdateHandler(Mat image)
        {
            if (image == null)
            {
                IsCameraRunning = false;
                StartButtonEnabled = true;
                StopButtonEnabled = false;
                Bitmap = null;
                return;
            }

            if (!IsCameraRunning)
                return;

            StartButtonEnabled = false;
            StopButtonEnabled = true;

            if (Camera == Camera.Face)
            {
                UpdateBitmap(image);
            }
        }

        public void EyeNewImageUpdateEventHandler(EyePipelineEvents.NewFrameEvent e)
        {
            if (IsCropMode)
                EyeImageUpdateHandler(e.image);
        }

        public void EyeNewTransformedUpdateEventHandler(EyePipelineEvents.NewTransformedFrameEvent e)
        {
            if (!IsCropMode)
                EyeImageUpdateHandler(e.image);
        }

        private void EyeImageUpdateHandler(Mat image)
        {
            if (image == null)
            {
                IsCameraRunning = false;
                StartButtonEnabled = true;
                StopButtonEnabled = false;
                Bitmap = null;
                return;
            }

            if (!IsCameraRunning)
                return;


            int channels = image.Channels();
            if (channels == 1)
            {
                var width = image.Width;
                var height = image.Height;
                switch (Camera)
                {
                    case Camera.Left:
                    {
                        var leftHalf = new OpenCvSharp.Rect(0, 0, width / 2, height);
                        var leftRoi = new Mat(image, leftHalf);


                        UpdateBitmap(leftRoi);
                        break;
                    }
                    case Camera.Right:
                    {
                        var rightHalf = new OpenCvSharp.Rect(width / 2, 0, width / 2, height);
                        var rightRoi = new Mat(image, rightHalf);
                        UpdateBitmap(rightRoi);
                        break;
                    }
                }
            }
            else if (channels == 2)
            {
                var images = image.Split();

                if (Camera == Camera.Left)
                    UpdateBitmap(images[0]);
                else if (Camera == Camera.Right)
                    UpdateBitmap(images[1]);
            }
        }

        void UpdateBitmap(Mat image)
        {
            if (_bitmap is null ||
                _bitmap.PixelSize.Width != image.Width ||
                _bitmap.PixelSize.Height != image.Height)
            {
                _bitmap = new WriteableBitmap(
                    new PixelSize(image.Width, image.Height),
                    new Vector(96, 96),
                    image.Channels() == 3 ? PixelFormats.Bgr24 : PixelFormats.Gray8,
                    AlphaFormat.Opaque);
            }

            CropManager.MaxSize.Height = _bitmap.PixelSize.Height;
            CropManager.MaxSize.Width = _bitmap.PixelSize.Width;

            if (!image.IsContinuous()) image = image.Clone();

            // scope for "using" a lock hehe...
            {
                using var frameBuffer = _bitmap.Lock();

                IntPtr srcPtr = image.Data;
                IntPtr destPtr = frameBuffer.Address;
                int size = image.Rows * image.Cols * image.ElemSize();

                unsafe
                {
                    Buffer.MemoryCopy(srcPtr.ToPointer(), destPtr.ToPointer(), size, size);
                }
            }

            IsCameraRunning = true;
            var tmp = Bitmap;
            Bitmap = null;
            Bitmap = tmp;
        }

        partial void OnFlipHorizontallyChanged(bool value)
        {
            Save();
        }

        partial void OnFlipVerticallyChanged(bool value)
        {
            Save();
        }

        partial void OnRotationChanged(float value)
        {
            Save();
        }

        void Save()
        {
            CameraSettings = new CameraSettings(
                Camera,
                CropManager.CropZone,
                Rotation,
                Gamma,
                false,
                FlipHorizontally,
                FlipVertically
            );
            _localSettingsService.SaveSetting(Name, CameraSettings);
            ImportantSettingsProperty = !ImportantSettingsProperty;
        }

        partial void OnGammaChanged(float value)
        {
            // If the slider is close enough to 1, then we treat it as 1
            Gamma = Math.Abs(value - 1) > 0.1f ? value : 1f;
            Save();
        }

        partial void OnIsCropModeChanged(bool value)
        {
            if (value)
            {
                CamViewMode = CamViewMode.Cropping;
            }
            else
            {
                CamViewMode = CamViewMode.Tracking;
            }
        }

        public void SelectWholeFrame()
        {
            CropManager.SelectEntireFrame(Camera);
            OnCropUpdated();
        }
    }

    // Necessary evil to store some globals that don't really have place to go :( _sob_
    private static bool _hasPerformedFirstTimeSetup = false;


    private int _messagesRecvd;
    [ObservableProperty] private string _messagesInPerSecCount;

    private int _messagesSent;
    [ObservableProperty] private string _messagesOutPerSecCount;

    [ObservableProperty] private bool _shouldEnableEyeCalibration;
    public TextBlock SelectedCalibrationTextBlock;

    public bool IsRunningAsAdmin => Utils.HasAdmin;

    [ObservableProperty] private bool _isInitialized = false;
    [ObservableProperty] private CameraControllerModel _leftCamera;
    [ObservableProperty] private CameraControllerModel _rightCamera;
    [ObservableProperty] private CameraControllerModel _faceCamera;


    private readonly DropOverlayService _dropOverlayService;

    private readonly FacePipelineManager _facePipelineManager;
    private readonly IFacePipelineEventBus _facePipelineEventBus;
    private readonly EyePipelineManager _eyePipelineManager;
    private readonly IEyePipelineEventBus _eyePipelineEventBus;
    private readonly IVROverlay _vrOverlay;
    private readonly IDeviceEnumerator _deviceEnumerator;
    private readonly ILocalSettingsService _localSettings;
    private readonly ILogger<HomePageViewModel> _logger;
    private readonly IPlatformConnector _platformConnector;

    public CalibrationRoutine.Routines RequestedVRCalibration = CalibrationRoutine.Map["BasicCalibration"];


    public HomePageViewModel(FacePipelineManager facePipelineManager,
        EyePipelineManager eyePipelineManager,
        IFacePipelineEventBus facePipelineEventBus,
        IEyePipelineEventBus eyePipelineEventBus,
        IVROverlay vrOverlay,
        IDeviceEnumerator deviceEnumerator,
        ILocalSettingsService localSettings,
        ILogger<HomePageViewModel> logger, DropOverlayService dropOverlayService, IPlatformConnector platformConnector)
    {
        _facePipelineManager = facePipelineManager;
        _eyePipelineManager = eyePipelineManager;
        _facePipelineEventBus = facePipelineEventBus;
        _eyePipelineEventBus = eyePipelineEventBus;
        _vrOverlay = vrOverlay;
        _deviceEnumerator = deviceEnumerator;
        _localSettings = localSettings;
        _logger = logger;
        _dropOverlayService = dropOverlayService;
        _platformConnector = platformConnector;

        _localSettings.Load(this);

        MessagesInPerSecCount = "0";
        MessagesOutPerSecCount = "0";

        Initialize();
    }

    private void Initialize()
    {
        bool hasRead = _localSettings.ReadSetting<bool>("SecondsWarningRead");
        if (!hasRead)
        {
            _dropOverlayService.Show();
        }

        var cameras = _deviceEnumerator.UpdateCameras();
        var cameraNames = cameras.Keys.ToArray();

        LeftCamera = new CameraControllerModel(_localSettings, _platformConnector, _deviceEnumerator, "LeftCamera",
            cameraNames, Camera.Left);
        RightCamera = new CameraControllerModel(_localSettings, _platformConnector, _deviceEnumerator, "RightCamera",
            cameraNames, Camera.Right);
        FaceCamera = new CameraControllerModel(_localSettings, _platformConnector, _deviceEnumerator, "FaceCamera",
            cameraNames, Camera.Face);

        FaceCamera.PropertyChanged += CameraControllerModel_PropertyChanged;
        LeftCamera.PropertyChanged += CameraControllerModel_PropertyChanged;
        RightCamera.PropertyChanged += CameraControllerModel_PropertyChanged;

        OnCameraModelUpdate(FaceCamera);
        OnCameraModelUpdate(LeftCamera);
        OnCameraModelUpdate(RightCamera);

        _facePipelineEventBus.Subscribe<FacePipelineEvents.NewFrameEvent>(FaceCamera.FaceNewImageUpdateEventHandler);
        _facePipelineEventBus.Subscribe<FacePipelineEvents.NewTransformedFrameEvent>(FaceCamera
            .FaceNewTransformedUpdateEventHandler);

        _eyePipelineEventBus.Subscribe<EyePipelineEvents.NewFrameEvent>(LeftCamera.EyeNewImageUpdateEventHandler);
        _eyePipelineEventBus.Subscribe<EyePipelineEvents.NewTransformedFrameEvent>(LeftCamera
            .EyeNewTransformedUpdateEventHandler);

        _eyePipelineEventBus.Subscribe<EyePipelineEvents.NewFrameEvent>(RightCamera.EyeNewImageUpdateEventHandler);
        _eyePipelineEventBus.Subscribe<EyePipelineEvents.NewTransformedFrameEvent>(RightCamera
            .EyeNewTransformedUpdateEventHandler);

        _facePipelineEventBus.Subscribe<FacePipelineEvents.ExceptionEvent>(FacePipelineExceptionHandler);
        _eyePipelineEventBus.Subscribe<EyePipelineEvents.ExceptionEvent>(EyePipelineExceptionHandler);

        IsInitialized = true;

        _ = TryStartCamerasAsync();
    }

    private void CameraControllerModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "ImportantSettingsProperty") return;

        if (sender is CameraControllerModel model)
            OnCameraModelUpdate(model);
    }

    private void OnCameraModelUpdate(CameraControllerModel model)
    {
        var copy = model.CameraSettings with { Roi = model.CameraSettings.Roi with { } };
        switch (model.Camera)
        {
            case Camera.Face:
                _facePipelineManager.SetTransformation(copy);
                break;
            case Camera.Left:
                _eyePipelineManager.SetLeftTransformation(copy);
                break;
            case Camera.Right:
                _eyePipelineManager.SetRightTransformation(copy);
                break;
        }
    }

    private void SetCameraRunning(CameraControllerModel model)
    {
        model.IsCameraRunning = true;
        SetButtons(model, false, true);
    }

    private async Task TryStartCamerasAsync()
    {
        if (!FaceCamera.IsCameraRunning && FaceCamera.ShouldAutostart)
            await StartCamera(FaceCamera);

        if (!LeftCamera.IsCameraRunning && LeftCamera.ShouldAutostart)
            await StartCamera(LeftCamera);

        if (!RightCamera.IsCameraRunning && RightCamera.ShouldAutostart)
            await StartCamera(RightCamera);
    }

    private void EyePipelineExceptionHandler(EyePipelineEvents.ExceptionEvent e)
    {
        LeftCamera.StartButtonEnabled = true;
        LeftCamera.StopButtonEnabled = false;
        LeftCamera.Bitmap = null;
        LeftCamera.IsCameraRunning = false;

        RightCamera.StartButtonEnabled = true;
        RightCamera.StopButtonEnabled = false;
        RightCamera.Bitmap = null;
        RightCamera.IsCameraRunning = false;
    }

    private void FacePipelineExceptionHandler(FacePipelineEvents.ExceptionEvent e)
    {
        FaceCamera.StartButtonEnabled = true;
        FaceCamera.StopButtonEnabled = false;

        FaceCamera.Bitmap = null;
        FaceCamera.IsCameraRunning = false;
    }

    [RelayCommand]
    public void StopCamera(CameraControllerModel model)
    {
        switch (model.Camera)
        {
            case Camera.Face:
                _facePipelineManager.StopCamera();
                FaceCamera.ShouldAutostart = false;
                SetButtons(FaceCamera, true, false);
                break;
            case Camera.Left:
                if (_eyePipelineManager.IsUsingSameCamera())
                {
                    _eyePipelineManager.StopAllCameras();
                    LeftCamera.IsCameraRunning = false;
                    RightCamera.IsCameraRunning = false;
                    SetButtons(LeftCamera, true, false);
                    SetButtons(RightCamera, true, false);
                    LeftCamera.ShouldAutostart = false;
                    RightCamera.ShouldAutostart = false;
                }
                else
                {
                    _eyePipelineManager.StopLeftCamera();
                    SetButtons(LeftCamera, true, false);
                    LeftCamera.ShouldAutostart = false;
                }

                break;
            case Camera.Right:
                if (_eyePipelineManager.IsUsingSameCamera())
                {
                    _eyePipelineManager.StopAllCameras();
                    LeftCamera.IsCameraRunning = false;
                    RightCamera.IsCameraRunning = false;
                    SetButtons(LeftCamera, true, false);
                    SetButtons(RightCamera, true, false);
                    LeftCamera.ShouldAutostart = false;
                    RightCamera.ShouldAutostart = false;
                }
                else
                {
                    _eyePipelineManager.StopRightCamera();
                    SetButtons(RightCamera, true, false);
                    RightCamera.ShouldAutostart = false;
                }

                break;
        }

        model.IsCameraRunning = false;
    }


    [RelayCommand]
    public async Task StartCamera(CameraControllerModel model)
    {
        try
        {
            SetButtons(model, false, false);
            var address = model.DisplayAddress;
            var backend = model.SelectedCaptureMethod;
            if (!model.CaptureMethodVisible || backend == Assets.Resources.Home_Backend_Default)
                backend = "";


            bool success = false;
            switch (model.Camera)
            {
                case Camera.Face:
                    success = await _facePipelineManager.TryStartIfNotRunning(address, backend);
                    FaceCamera.ShouldAutostart = true;
                    break;
                case Camera.Left:
                    success = await _eyePipelineManager.TryStartLeftIfNotRunning(address, backend);
                    LeftCamera.ShouldAutostart = true;
                    break;
                case Camera.Right:
                    success = await _eyePipelineManager.TryStartRightIfNotRunning(address, backend);
                    RightCamera.ShouldAutostart = true;
                    break;
            }

            if (success)
            {
                SetCameraRunning(model);
                _localSettings.SaveSetting("LastOpened" + model.Name, model.DisplayAddress);
            }
            else
            {
                SetButtons(model, true, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("{}", ex);
            SetButtons(model, true, false);
        }
    }

    private void SetButtons(CameraControllerModel model, bool startEnabled, bool stopEnabled)
    {
        model.StartButtonEnabled = startEnabled;
        model.StopButtonEnabled = stopEnabled;
    }


    [RelayCommand]
    private void SelectWholeFrame(CameraControllerModel model)
    {
        model.SelectWholeFrame();
    }

    [RelayCommand]
    private async Task RequestVRCalibration()
    {
        var res = await Task.Run(async () =>
            {
                try
                {
                    return await _vrOverlay.EyeTrackingCalibrationRequested(RequestedVRCalibration);
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            }
        );
        if (res.Item1)
        {
            SelectedCalibrationTextBlock.Foreground = new SolidColorBrush(Colors.Green);
        }
        else
        {
            SelectedCalibrationTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            _logger.LogError(res.Item2);
        }

        var previousText = SelectedCalibrationTextBlock.Text;
        SelectedCalibrationTextBlock.Text = res.Item2;
        await Task.Delay(5000);
        SelectedCalibrationTextBlock.Text = previousText;
        SelectedCalibrationTextBlock.Foreground = new SolidColorBrush(GetBaseHighColor());
    }

    public Color GetBaseHighColor()
    {
        Color color = Colors.White;
        switch (Application.Current!.ActualThemeVariant.ToString())
        {
            case "Light":
                color = Colors.Black;
                break;
            case "Dark":
                color = Colors.White;
                break;
        }

        return color;
    }

    public void Dispose()
    {
        CleanupResources();
    }

    public void OnCropUpdated(CameraControllerModel model)
    {
        var copy = model.CameraSettings with { Roi = model.CameraSettings.Roi with { } };
        switch (model.Camera)
        {
            case Camera.Face:
                _facePipelineManager.SetTransformation(copy);
                break;
            case Camera.Left:
                _eyePipelineManager.SetLeftTransformation(copy);
                break;
            case Camera.Right:
                _eyePipelineManager.SetRightTransformation(copy);
                break;
        }
    }

    [RelayCommand]
    public async Task ReloadEyeInference()
    {
        await _eyePipelineManager.LoadInferenceAsync();
    }

    private bool _disposed = false;

    private void CleanupResources()
    {
        if (_disposed) return;
        FaceCamera.CamViewMode = CamViewMode.Tracking;
        LeftCamera.CamViewMode = CamViewMode.Tracking;
        RightCamera.CamViewMode = CamViewMode.Tracking;

        _facePipelineEventBus.Unsubscribe<FacePipelineEvents.NewFrameEvent>(FaceCamera.FaceNewImageUpdateEventHandler);
        _facePipelineEventBus.Unsubscribe<FacePipelineEvents.NewTransformedFrameEvent>(FaceCamera
            .FaceNewTransformedUpdateEventHandler);

        _eyePipelineEventBus.Unsubscribe<EyePipelineEvents.NewFrameEvent>(LeftCamera.EyeNewImageUpdateEventHandler);
        _eyePipelineEventBus.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(LeftCamera
            .EyeNewTransformedUpdateEventHandler);

        _eyePipelineEventBus.Unsubscribe<EyePipelineEvents.NewFrameEvent>(RightCamera.EyeNewImageUpdateEventHandler);
        _eyePipelineEventBus.Unsubscribe<EyePipelineEvents.NewTransformedFrameEvent>(RightCamera
            .EyeNewTransformedUpdateEventHandler);

        _facePipelineEventBus.Unsubscribe<FacePipelineEvents.ExceptionEvent>(FacePipelineExceptionHandler);
        _eyePipelineEventBus.Unsubscribe<EyePipelineEvents.ExceptionEvent>(EyePipelineExceptionHandler);
    }
}
