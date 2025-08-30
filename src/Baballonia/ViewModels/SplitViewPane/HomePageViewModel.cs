using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Baballonia.Contracts;
using Baballonia.Helpers;
using Baballonia.Services;
using Baballonia.Services.Inference;
using Baballonia.Services.Inference.Enums;
using Baballonia.Services.Inference.Models;
using Baballonia.Services.Inference.VideoSources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Buffer = System.Buffer;
using Rect = Avalonia.Rect;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class HomePageViewModel : ViewModelBase, IDisposable
{
    // This feels unorthodox but... i kinda like it?
    public partial class CameraControllerModel : ObservableObject, IDisposable
    {
        public string Name;
        public readonly CropManager CropManager = new();
        public CamViewMode CamViewMode = CamViewMode.Tracking;
        public readonly Camera Camera;

        [ObservableProperty] private WriteableBitmap? _bitmap;

        [ObservableProperty] private bool _startButtonEnabled = true;
        [ObservableProperty] private bool _stopButtonEnabled = false;
        [ObservableProperty] private bool _hintEnabled = false;
        [ObservableProperty] private bool _inferEnabled = false;
        [ObservableProperty] private string _displayAddress;
        [ObservableProperty] private Rect _overlayRectangle;
        [ObservableProperty] private bool _flipHorizontally = false;
        [ObservableProperty] private bool _flipVertically = false;
        [ObservableProperty] private float _rotation = 0f;
        [ObservableProperty] private float _gamma = 1f;
        [ObservableProperty] private bool _isCropMode = false;
        [ObservableProperty] private bool _isCameraRunning = false;
        public ObservableCollection<string> Suggestions { get; set; } = [];

        private readonly ILocalSettingsService _localSettingsService;
        private readonly DefaultProcessingPipeline _processingPipeline;

        public CameraControllerModel(ILocalSettingsService localSettingsService, string name,
            DefaultProcessingPipeline processingPipeline, string[] cameras, Camera camera)
        {
            _localSettingsService = localSettingsService;
            _processingPipeline = processingPipeline;
            Name = name;
            Camera = camera;

            _ = InitializeAsync(cameras);

            _processingPipeline.TransformedFrameEvent += ImageUpdateEventHandler;
        }

        private async Task InitializeAsync(string[] cameras)
        {
            var displayAddress = await _localSettingsService.ReadSettingAsync<string>("LastOpened" + Name);
            var camSettings = await _localSettingsService.ReadSettingAsync<CameraSettings>(Name);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateCameraDropDown(cameras);

                DisplayAddress = displayAddress;
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
                        return;
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
                                return;
                            default:
                                IsCameraRunning = true;
                                StartButtonEnabled = false;
                                StopButtonEnabled = true;
                                break;
                        }

                        break;
                }
            });
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

        partial void OnBitmapChanged(WriteableBitmap? value)
        {
            // IsCameraRunning = value != null;
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
                _localSettingsService.SaveSettingAsync(Name, transformer.Transformation);
            }
            else if (t is DualImageTransformer dualTransformer)
            {
                if (Camera == Camera.Left)
                    _localSettingsService.SaveSettingAsync(Name, dualTransformer.LeftTransformer.Transformation);
                if (Camera == Camera.Right)
                    _localSettingsService.SaveSettingAsync(Name, dualTransformer.RightTransformer.Transformation);
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
    private ILocalSettingsService LocalSettingsService { get; }

    private int _messagesRecvd;
    [ObservableProperty] private string _messagesInPerSecCount;

    private int _messagesSent;
    [ObservableProperty] private string _messagesOutPerSecCount;

    [ObservableProperty] private bool _shouldShowEyeCalibration;
    [ObservableProperty] private string _selectedCalibrationText;

    [ObservableProperty] private CameraControllerModel _leftCamera;
    [ObservableProperty] private CameraControllerModel _rightCamera;
    [ObservableProperty] private CameraControllerModel _faceCamera;
    public readonly TaskCompletionSource camerasInitialized = new();

    private readonly DispatcherTimer _msgCounterTimer;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ProcessingLoopService _processingLoopService;


    private ILogger<HomePageViewModel> _logger;

    public HomePageViewModel()
    {
        OscTarget = Ioc.Default.GetService<IOscTarget>()!;
        OscRecvService = Ioc.Default.GetService<OscRecvService>()!;
        OscSendService = Ioc.Default.GetService<OscSendService>()!;
        LocalSettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _localSettingsService = Ioc.Default.GetRequiredService<ILocalSettingsService>()!;
        _processingLoopService = Ioc.Default.GetService<ProcessingLoopService>()!;
        _logger = Ioc.Default.GetService<ILogger<HomePageViewModel>>()!;
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

        Task.Run(async () =>
        {
            var cameras = await App.DeviceEnumerator.UpdateCameras();
            var cameraNames = cameras.Keys.ToArray();
            LeftCamera = new CameraControllerModel(_localSettingsService, "LeftCamera",
                _processingLoopService.EyesProcessingPipeline, cameraNames, Camera.Left);
            RightCamera = new CameraControllerModel(_localSettingsService, "RightCamera",
                _processingLoopService.EyesProcessingPipeline, cameraNames, Camera.Right);
            FaceCamera = new CameraControllerModel(_localSettingsService, "FaceCamera",
                _processingLoopService.FaceProcessingPipeline, cameraNames, Camera.Face);
            camerasInitialized.SetResult();
        });

        _processingLoopService.PipelineExceptionEvent += PipelineExceptionEventHandler;
    }

    private void PipelineExceptionEventHandler(Exception ex)
    {
        if (_processingLoopService.FaceProcessingPipeline.VideoSource == null)
        {
            FaceCamera.StartButtonEnabled = true;
            FaceCamera.StopButtonEnabled = false;

            FaceCamera.Bitmap = null;
            FaceCamera.IsCameraRunning = false;
        }

        if (_processingLoopService.EyesProcessingPipeline.VideoSource == null)
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

    private async Task<IVideoSource?> StartCameraAsync(string address)
    {
        var camera = address;
        if (string.IsNullOrEmpty(camera)) return null;

        App.DeviceEnumerator.Cameras ??= await App.DeviceEnumerator.UpdateCameras();

        if (App.DeviceEnumerator.Cameras.TryGetValue(camera, out var mappedAddress))
        {
            camera = mappedAddress;
        }

        return await Task.Run<IVideoSource?>(() =>
        {
            var cameraSource = new SingleCameraSourceFactory().Create(camera);
            if (cameraSource == null)
                return null;

            if (!cameraSource.Start())
            {
                _logger.LogError("Could not initialize {}", address);
                return null;
            }

            Stopwatch sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(3);
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
        var pipeline = _processingLoopService.EyesProcessingPipeline;
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


    private SemaphoreSlim _cameraStartLock = new(1, 1);
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task StartCamera(CameraControllerModel model)
    {
        SetButtons(model, false, false);

        if (model.Camera == Camera.Face)
        {
            await StartCameraAsync(model);
            return;
        }

        await _cameraStartLock.WaitAsync();
        try
        {
            await StartCameraAsync(model);
        }
        finally
        {
            _cameraStartLock.Release();
        }
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

        if (_processingLoopService.EyesProcessingPipeline.VideoSource is DualCameraSource dualSource)
        {
            if (type == Camera.Left && dualSource.RightCam != null)
                _processingLoopService.EyesProcessingPipeline.VideoSource = dualSource.RightCam;
            else if (dualSource.LeftCam != null)
                _processingLoopService.EyesProcessingPipeline.VideoSource = dualSource.LeftCam;
            else
                return false;

            model.IsCameraRunning = true;
            return true;
        }

        return false;
    }

    private Task SaveLastOpenedAsync(CameraControllerModel model)
    {
        return _localSettingsService.SaveSettingAsync("LastOpened" + model.Name, model.DisplayAddress);
    }

    private void SetButtons(CameraControllerModel model, bool startEnabled, bool stopEnabled)
    {
        model.StartButtonEnabled = startEnabled;
        model.StopButtonEnabled = stopEnabled;
    }


    private async Task StartCameraAsync(CameraControllerModel model)
    {
        var type = model.Camera;

        var isSameEye = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (type == Camera.Face || !IsSameAddressAsOtherEye(model)) return false;

            var tryRes = TryHandleSameEyeCamera(model);
            if (tryRes)
                SetButtons(model, false, true);
            return tryRes;
        });
        if (isSameEye)
        {
            await SaveLastOpenedAsync(model);
            return;
        }

        var cameraSource = await StartCameraAsync(model.DisplayAddress);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cameraSource == null)
            {
                SetButtons(model, true, false);
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
        });

        await SaveLastOpenedAsync(model);
    }

    private void UpdateFacePipeline(IVideoSource cameraSource)
    {
        var pipeline = _processingLoopService.FaceProcessingPipeline;

        if (pipeline.VideoSource != null)
        {
            pipeline.VideoSource.Dispose();
            pipeline.VideoSource = null;
        }

        pipeline.VideoSource = cameraSource;
    }

    private void UpdateEyePipeline(IVideoSource cameraSource, Camera type)
    {
        var pipeline = _processingLoopService.EyesProcessingPipeline;

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
        await App.Overlay.EyeTrackingCalibrationRequested(CalibrationRoutine.QuickCalibration);
        await _localSettingsService.SaveSettingAsync("EyeHome_EyeModel", "tuned_temporal_eye_tracking.onnx");
        await _processingLoopService.SetupEyeInference();
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

        _processingLoopService.PipelineExceptionEvent -= PipelineExceptionEventHandler;
        OscSendService.OnMessagesDispatched -= MessageDispatched;
        _msgCounterTimer.Stop();
    }
}
