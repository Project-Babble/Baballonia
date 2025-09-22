using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Factories;
using Baballonia.Helpers;
using Baballonia.Services;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.Platforms;
using Baballonia.Services.Inference.VideoSources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using Buffer = System.Buffer;
using Rect = Avalonia.Rect;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class HomePageViewModel : ViewModelBase, IDisposable
{
    // This feels unorthodox but... i kinda like it?
    public partial class CameraControllerModel : ObservableObject, IDisposable
    {
        public readonly TaskCompletionSource IsInitialized = new();

        public string Name;
        public readonly CropManager CropManager = new();
        public CamViewMode CamViewMode = CamViewMode.Tracking;
        public readonly Camera Camera;

        [ObservableProperty] private WriteableBitmap? _bitmap;

        [ObservableProperty] private bool _startButtonEnabled = true;
        [ObservableProperty] private bool _stopButtonEnabled = false;
        [ObservableProperty] private bool _hintEnabled = false;
        [ObservableProperty] private string _displayAddress;
        [ObservableProperty] private Rect _overlayRectangle;
        [ObservableProperty] private bool _flipHorizontally = false;
        [ObservableProperty] private bool _flipVertically = false;
        [ObservableProperty] private float _rotation = 0f;
        [ObservableProperty] private float _gamma = 1f;
        [ObservableProperty] private bool _isCropMode = false;
        [ObservableProperty] private bool _isCameraRunning = false;
        [ObservableProperty] private int _selectedCaptureMethod = -1;
        [ObservableProperty] private bool _captureMethodVisible = false;
        public ObservableCollection<string> Suggestions { get; set; } = [];
        public ObservableCollection<string> CaptureMethods { get; set; } = [];

        private readonly ILocalSettingsService LocalSettingsService;
        private readonly DefaultProcessingPipeline _processingPipeline;

        private Stopwatch _deviceUpdateDebounce = new();

        public CameraControllerModel(ILocalSettingsService localSettingsService, string name,
            DefaultProcessingPipeline processingPipeline, string[] cameras, Camera camera)
        {
            LocalSettingsService = localSettingsService;
            _processingPipeline = processingPipeline;
            Name = name;
            Camera = camera;

            Initialize(cameras);

            _deviceUpdateDebounce.Start();

            _processingPipeline.TransformedFrameEvent += ImageUpdateEventHandler;
        }


        partial void OnDisplayAddressChanged(string value)
        {
            if (PlatformConnector.Captures.Count <= 0) PlatformConnectorFactory.Create(NullLogger.Instance, "temp");

            var matches = PlatformConnector.Captures.Where(i => i.Key.CanConnect(value)).ToArray();

            var shouldShow = matches.Length >= 2;
            CaptureMethodVisible = shouldShow;

            CaptureMethods.Clear();
            if (shouldShow)
            {
                CaptureMethods.Add(Assets.Resources.Home_Backend_Default);
                foreach (var match in matches)
                    CaptureMethods.Add(match.Value.Name);
            }

            SelectedCaptureMethod = shouldShow ? 0 : -1;
        }

        private void Initialize(string[] cameras)
        {
            var displayAddress = LocalSettingsService.ReadSetting<string>("LastOpened" + Name);
            var preferredCapture = LocalSettingsService.ReadSetting<string>("LastOpenedPreferredCapture" + Name);
            var camSettings = LocalSettingsService.ReadSetting<CameraSettings>(Name);

            UpdateCameraDropDown(cameras);
            DisplayAddress = displayAddress;
            var selectedIndex = CaptureMethods.IndexOf(preferredCapture);
            if (selectedIndex != -1) SelectedCaptureMethod = selectedIndex;
            FlipHorizontally = camSettings.UseHorizontalFlip;
            FlipVertically = camSettings.UseVerticalFlip;
            Rotation = camSettings.RotationRadians;
            Gamma = camSettings.Gamma;

            CropManager.SetCropZone(camSettings.Roi);
            OverlayRectangle = CropManager.CropZone.GetRect();
            OnCropUpdated();

            switch (_processingPipeline.VideoSource)
            {
                case null:
                    break;
                case SingleCameraSource singleCamera:
                    IsCameraRunning = true;
                    StartButtonEnabled = false;
                    StopButtonEnabled = true;
                    break;
                case DualCameraSource dualCamera:
                    switch (Camera)
                    {
                        case Camera.Left when dualCamera.LeftCam == null:
                        case Camera.Right when dualCamera.RightCam == null:
                            break;
                        default:
                            IsCameraRunning = true;
                            StartButtonEnabled = false;
                            StopButtonEnabled = true;
                            break;
                    }

                    break;
            }

            IsInitialized.SetResult();
        }

        public void UpdateCameraDropDown()
        {
            if (_deviceUpdateDebounce.Elapsed < TimeSpan.FromSeconds(5)) return;
            _deviceUpdateDebounce.Restart();

            var cameras = App.DeviceEnumerator.UpdateCameras();
            var cameraNames = cameras.Keys.ToArray();
            UpdateCameraDropDown(cameraNames);
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
            var t = _processingPipeline.ImageTransformer;
            if (t is ImageTransformer transformer)
            {
                transformer.Transformation.Roi = CropManager.CropZone;
            }
            else if (t is DualImageTransformer dualTransformer)
            {
                if (Camera == Camera.Left)
                    dualTransformer.LeftTransformer.Transformation.Roi = CropManager.CropZone;
                if (Camera == Camera.Right)
                    dualTransformer.RightTransformer.Transformation.Roi = CropManager.CropZone;
            }

            SaveTransformer();
        }

        [RelayCommand]
        public void StopCamera()
        {
            _processingPipeline.VideoSource?.Dispose();
            _processingPipeline.VideoSource = null;

            Bitmap = null;

            IsCameraRunning = false;
            StartButtonEnabled = true;
            StopButtonEnabled = false;
        }

        public string SelectedPreferredCapture =>
            CaptureMethods.Count <= 0 || (SelectedCaptureMethod < 0 || SelectedCaptureMethod >= CaptureMethods.Count)
                ? "Default"
                : CaptureMethods[SelectedCaptureMethod];
        void ImageUpdateEventHandler(Mat image)
        {
            if (image == null)
            {
                IsCameraRunning = false;
                Bitmap = null;
                return;
            }

            if (!IsCameraRunning)
                return;

            if (Camera == Camera.Face)
            {
                UpdateBitmap(image);
                return;
            }

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
            var t = _processingPipeline.ImageTransformer;
            if (t is ImageTransformer transformer)
            {
                transformer.Transformation.UseHorizontalFlip = value;
            }
            else if (t is DualImageTransformer dualTransformer)
            {
                if (Camera == Camera.Left)
                    dualTransformer.LeftTransformer.Transformation.UseHorizontalFlip = value;
                if (Camera == Camera.Right)
                    dualTransformer.RightTransformer.Transformation.UseHorizontalFlip = value;
            }

            SaveTransformer();
        }

        partial void OnFlipVerticallyChanged(bool value)
        {
            var t = _processingPipeline.ImageTransformer;
            if (t is ImageTransformer transformer)
            {
                transformer.Transformation.UseVerticalFlip = value;
            }
            else if (t is DualImageTransformer dualTransformer)
            {
                if (Camera == Camera.Left)
                    dualTransformer.LeftTransformer.Transformation.UseVerticalFlip = value;
                if (Camera == Camera.Right)
                    dualTransformer.RightTransformer.Transformation.UseVerticalFlip = value;
            }

            SaveTransformer();
        }

        partial void OnRotationChanged(float value)
        {
            var t = _processingPipeline.ImageTransformer;
            if (t is ImageTransformer transformer)
            {
                transformer.Transformation.RotationRadians = value;
            }
            else if (t is DualImageTransformer dualTransformer)
            {
                if (Camera == Camera.Left)
                    dualTransformer.LeftTransformer.Transformation.RotationRadians = value;
                if (Camera == Camera.Right)
                    dualTransformer.RightTransformer.Transformation.RotationRadians = value;
            }

            SaveTransformer();
        }

        void SaveTransformer()
        {
            var t = _processingPipeline.ImageTransformer;
            if (t is ImageTransformer transformer)
            {
                LocalSettingsService.SaveSetting(Name, transformer.Transformation);
            }
            else if (t is DualImageTransformer dualTransformer)
            {
                if (Camera == Camera.Left)
                    LocalSettingsService.SaveSetting(Name, dualTransformer.LeftTransformer.Transformation);
                if (Camera == Camera.Right)
                    LocalSettingsService.SaveSetting(Name, dualTransformer.RightTransformer.Transformation);
            }
        }

        partial void OnGammaChanged(float value)
        {
            // If the slider is close enough to 1, then we treat it as 1
            value = Math.Abs(value - 1) > 0.1f ? value : 1f;

            var t = _processingPipeline.ImageTransformer;
            if (t is ImageTransformer transformer)
            {
                transformer.Transformation.Gamma = value;
            }
            else if (t is DualImageTransformer dualTransformer)
            {
                if (Camera == Camera.Left)
                    dualTransformer.LeftTransformer.Transformation.Gamma = value;
                if (Camera == Camera.Right)
                    dualTransformer.RightTransformer.Transformation.Gamma = value;
            }

            SaveTransformer();
        }

        partial void OnIsCropModeChanged(bool value)
        {
            if (value)
            {
                _processingPipeline.TransformedFrameEvent -= ImageUpdateEventHandler;
                _processingPipeline.NewFrameEvent += ImageUpdateEventHandler;
                CamViewMode = CamViewMode.Cropping;
            }
            else
            {
                _processingPipeline.NewFrameEvent -= ImageUpdateEventHandler;
                _processingPipeline.TransformedFrameEvent += ImageUpdateEventHandler;
                CamViewMode = CamViewMode.Tracking;
            }
        }

        public void SelectWholeFrame()
        {
            CropManager.SelectEntireFrame(Camera);
            OnCropUpdated();
        }

        private bool _disposed = false;

        public void Dispose()
        {
            if (_disposed) return;

            _processingPipeline.TransformedFrameEvent -= ImageUpdateEventHandler;
            _processingPipeline.NewFrameEvent -= ImageUpdateEventHandler;
        }
    }

    private static bool _hasPerformedFirstTimeSetup = false;

    public IOscTarget OscTarget { get; }
    private OscRecvService OscRecvService { get; }
    private OscSendService OscSendService { get; }

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

    public readonly TaskCompletionSource CamerasInitialized = new();

    public readonly ILocalSettingsService LocalSettingsService;
    public readonly ProcessingLoopService ProcessingLoopService;

    private readonly DispatcherTimer _msgCounterTimer;
    private readonly DropOverlayService _dropOverlayService;

    public string RequestedVRCalibration = CalibrationRoutine.Map["QuickCalibration"];

    private ILogger<HomePageViewModel> _logger;

    public HomePageViewModel()
    {
        OscTarget = Ioc.Default.GetService<IOscTarget>()!;
        OscRecvService = Ioc.Default.GetService<OscRecvService>()!;
        OscSendService = Ioc.Default.GetService<OscSendService>()!;
        LocalSettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        LocalSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>()!;
        ProcessingLoopService = Ioc.Default.GetService<ProcessingLoopService>()!;
        _logger = Ioc.Default.GetService<ILogger<HomePageViewModel>>()!;
        _dropOverlayService = Ioc.Default.GetService<DropOverlayService>()!;

        LocalSettingsService.Load(this);

        MessagesInPerSecCount = "0";
        MessagesOutPerSecCount = "0";
        OscSendService.OnMessagesDispatched += MessageDispatched;

        _msgCounterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _msgCounterTimer.Tick += (_, _) =>
        {
            MessagesInPerSecCount = _messagesRecvd.ToString();
            _messagesRecvd = 0;

            MessagesOutPerSecCount = _messagesSent.ToString();
            _messagesSent = 0;
        };
        _msgCounterTimer.Start();

        Initialize();

        ProcessingLoopService.PipelineExceptionEvent += PipelineExceptionEventHandler;
    }

    private void Initialize()
    {
        bool hasRead = LocalSettingsService.ReadSetting<bool>("SecondsWarningRead");
        if (!hasRead)
        {
            _dropOverlayService.Show();
        }

        var cameras = App.DeviceEnumerator.UpdateCameras();
        var cameraNames = cameras.Keys.ToArray();

        LeftCamera = new CameraControllerModel(LocalSettingsService, "LeftCamera",
            ProcessingLoopService.EyesProcessingPipeline, cameraNames, Camera.Left);
        RightCamera = new CameraControllerModel(LocalSettingsService, "RightCamera",
            ProcessingLoopService.EyesProcessingPipeline, cameraNames, Camera.Right);
        FaceCamera = new CameraControllerModel(LocalSettingsService, "FaceCamera",
            ProcessingLoopService.FaceProcessingPipeline, cameraNames, Camera.Face);

        IsInitialized = true;

        _ = TryStartCamerasAsync();
    }

    private async Task TryStartCamerasAsync()
    {
        await LeftCamera.IsInitialized.Task;
        if (!LeftCamera.IsCameraRunning)
            await StartCameraAsync(LeftCamera);
        await RightCamera.IsInitialized.Task;
        if (!RightCamera.IsCameraRunning)
            await StartCameraAsync(RightCamera);
        await FaceCamera.IsInitialized.Task;
        if (!FaceCamera.IsCameraRunning)
            await StartCameraAsync(FaceCamera);
    }

    private void PipelineExceptionEventHandler(Exception ex)
    {
        if (ProcessingLoopService.FaceProcessingPipeline.VideoSource == null)
        {
            FaceCamera.StartButtonEnabled = true;
            FaceCamera.StopButtonEnabled = false;

            FaceCamera.Bitmap = null;
            FaceCamera.IsCameraRunning = false;
        }

        if (ProcessingLoopService.EyesProcessingPipeline.VideoSource == null)
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
    }

    private async Task<IVideoSource?> StartCameraAsync(string address, string preferredCapture = "")
    {
        var camera = address;
        if (string.IsNullOrEmpty(camera)) return null;

        App.DeviceEnumerator.Cameras ??= App.DeviceEnumerator.UpdateCameras();

        if (App.DeviceEnumerator.Cameras.TryGetValue(camera, out var mappedAddress))
        {
            camera = mappedAddress;
        }

        return await Task.Run<IVideoSource?>(() =>
        {
            var cameraSource = SingleCameraSourceFactory.Create(camera, preferredCapture);
            if (cameraSource == null)
                return null;

            if (!cameraSource.Start())
            {
                _logger.LogError("Could not initialize {}", address);
                return null;
            }

            Stopwatch sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(13);
            while (sw.Elapsed < timeout)
            {
                var testFrame = cameraSource.GetFrame();
                if (testFrame != null)
                    return cameraSource;
            }

            _logger.LogError("No data was received from {}, closing...", address);
            cameraSource.Dispose();
            return null;
        });
    }

    [RelayCommand]
    public void StopCamera(CameraControllerModel model)
    {
        var pipeline = ProcessingLoopService.EyesProcessingPipeline;
        switch (pipeline.VideoSource)
        {
            case SingleCameraSource singleCameraSource:
                singleCameraSource.Dispose();
                pipeline.VideoSource = null;

                LeftCamera.IsCameraRunning = false;
                LeftCamera.StartButtonEnabled = true;
                LeftCamera.StopButtonEnabled = false;

                RightCamera.IsCameraRunning = false;
                RightCamera.StartButtonEnabled = true;
                RightCamera.StopButtonEnabled = false;
                break;
            case DualCameraSource dualCameraSource:
                if (model.Camera == Camera.Right)
                {
                    dualCameraSource.RightCam?.Dispose();
                    dualCameraSource.RightCam = null;

                    RightCamera.IsCameraRunning = false;
                    RightCamera.StartButtonEnabled = true;
                    RightCamera.StopButtonEnabled = false;
                }
                else if (model.Camera == Camera.Left)
                {
                    dualCameraSource.LeftCam?.Dispose();
                    dualCameraSource.LeftCam = null;

                    LeftCamera.IsCameraRunning = false;
                    LeftCamera.StartButtonEnabled = true;
                    LeftCamera.StopButtonEnabled = false;
                }

                break;
        }
    }


    [RelayCommand]
    public async Task StartCamera(CameraControllerModel model)
    {
        SetButtons(model, false, false);
        await StartCameraAsync(model);
    }

    private bool IsSameAddressAsOtherEye(CameraControllerModel model)
    {
        var type = model.Camera;
        if (type == Camera.Left)
            return !string.IsNullOrEmpty(model.DisplayAddress) &&
                   string.Equals(model.DisplayAddress, RightCamera.DisplayAddress);
        if (type == Camera.Right)
            return !string.IsNullOrEmpty(model.DisplayAddress) &&
                   string.Equals(model.DisplayAddress, LeftCamera.DisplayAddress);
        return false;
    }

    private bool TryHandleSameEyeCamera(CameraControllerModel model)
    {
        var type = model.Camera;

        if (ProcessingLoopService.EyesProcessingPipeline.VideoSource is DualCameraSource dualSource)
        {
            if (type == Camera.Left && dualSource.RightCam != null)
                ProcessingLoopService.EyesProcessingPipeline.VideoSource = dualSource.RightCam;
            else if (dualSource.LeftCam != null)
                ProcessingLoopService.EyesProcessingPipeline.VideoSource = dualSource.LeftCam;
            else
                return false;

            model.IsCameraRunning = true;
            return true;
        }

        return false;
    }

    private void SaveLastOpened(CameraControllerModel model)
    {
        LocalSettingsService.SaveSetting("LastOpened" + model.Name, model.DisplayAddress);
        LocalSettingsService.SaveSetting("LastOpenedPreferredCapture" + model.Name, model.SelectedPreferredCapture);
    }

    private void SetButtons(CameraControllerModel model, bool startEnabled, bool stopEnabled)
    {
        model.StartButtonEnabled = startEnabled;
        model.StopButtonEnabled = stopEnabled;
    }


    private bool IsSameEye(CameraControllerModel model)
    {
        var type = model.Camera;
        if (type == Camera.Face || !IsSameAddressAsOtherEye(model)) return false;

        var tryRes = TryHandleSameEyeCamera(model);
        if (tryRes)
            SetButtons(model, false, true);
        return tryRes;
    }

    private async Task StartCameraAsync(CameraControllerModel model)
    {
        if (IsSameEye(model))
        {
            SaveLastOpened(model);
            return;
        }

        var type = model.Camera;

        var cameraSource = await StartCameraAsync(model.DisplayAddress, model.SelectedPreferredCapture);

        if (cameraSource == null)
        {
            SetButtons(model, true, false);
            SaveLastOpened(model);
            return;
        }

        if (type == Camera.Face)
        {
            UpdateFacePipeline(cameraSource);
        }
        else
        {
            UpdateEyePipeline(cameraSource, type);
        }

        model.IsCameraRunning = true;
        SetButtons(model, false, true);

        SaveLastOpened(model);
    }

    private void UpdateFacePipeline(IVideoSource cameraSource)
    {
        var pipeline = ProcessingLoopService.FaceProcessingPipeline;

        if (pipeline.VideoSource != null)
        {
            pipeline.VideoSource.Dispose();
            pipeline.VideoSource = null;
        }

        pipeline.VideoSource = cameraSource;
    }

    private void UpdateEyePipeline(IVideoSource cameraSource, Camera type)
    {
        var pipeline = ProcessingLoopService.EyesProcessingPipeline;

        if (pipeline.VideoSource == null)
        {
            var dualSource = new DualCameraSource();
            if (type == Camera.Left)
                dualSource.LeftCam = cameraSource;
            else
                dualSource.RightCam = cameraSource;

            pipeline.VideoSource = dualSource;
        }
        else if (pipeline.VideoSource is DualCameraSource dualSource)
        {
            if (type == Camera.Left)
                dualSource.LeftCam = cameraSource;
            else
                dualSource.RightCam = cameraSource;
        }
        else if (pipeline.VideoSource is SingleCameraSource singleSource)
        {
            var dual = new DualCameraSource();
            if (type == Camera.Left)
            {
                dual.LeftCam = cameraSource;
                dual.RightCam = singleSource;
            }
            else
            {
                dual.RightCam = cameraSource;
                dual.LeftCam = singleSource;
            }

            pipeline.VideoSource = dual;
        }
    }

    [RelayCommand]
    private void SelectWholeFrame(CameraControllerModel model)
    {
        model.SelectWholeFrame();
    }

    [RelayCommand]
    private async Task RequestVRCalibration()
    {
        var res = await App.Overlay.EyeTrackingCalibrationRequested(RequestedVRCalibration);
        if (res.success)
        {
            if (!Directory.Exists(Utils.ModelsDirectory))
            {
                Directory.CreateDirectory(Utils.ModelsDirectory);
            }

            var destPath = Path.Combine(Utils.ModelsDirectory, $"tuned_temporal_eye_tracking_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.onnx");
            File.Move("tuned_temporal_eye_tracking.onnx", destPath);
            LocalSettingsService.SaveSetting("EyeHome_EyeModel", destPath);
            var eye = await ProcessingLoopService.LoadEyeInferenceAsync();
            ProcessingLoopService.EyesProcessingPipeline.InferenceService = eye;
            SelectedCalibrationTextBlock.Foreground = new SolidColorBrush(Colors.Green);
        }
        else
        {
            SelectedCalibrationTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            _logger.LogError(res.status);
        }

        var previousText = SelectedCalibrationTextBlock.Text;
        SelectedCalibrationTextBlock.Text = res.status;
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

    private void MessageDispatched(int msgCount) => _messagesSent += msgCount;

    public void Dispose()
    {
        CleanupResources();
    }

    private bool _disposed = false;

    private void CleanupResources()
    {
        if (_disposed) return;
        FaceCamera.CamViewMode = CamViewMode.Tracking;
        LeftCamera.CamViewMode = CamViewMode.Tracking;
        RightCamera.CamViewMode = CamViewMode.Tracking;

        _faceCamera.Dispose();
        _leftCamera.Dispose();
        _rightCamera.Dispose();

        ProcessingLoopService.PipelineExceptionEvent -= PipelineExceptionEventHandler;
        OscSendService.OnMessagesDispatched -= MessageDispatched;
        _msgCounterTimer.Stop();
    }
}
