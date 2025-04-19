using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.Helpers;
using AvaloniaMiaDev.OSC;
using AvaloniaMiaDev.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace AvaloniaMiaDev.ViewModels.SplitViewPane;

public partial class HomePageViewModel : ViewModelBase
{
    // Camera properties
    public WriteableBitmap LeftEyeBitmap { get; set; }
    public WriteableBitmap RightEyeBitmap { get; set; }
    public WriteableBitmap FaceBitmap { get; set; }

    [ObservableProperty]
    [property: SavedSetting("EyeHome_EyeModel", "eyeModel.onnx")]
    private string _eyeModel;

    // Left eye properties
    [ObservableProperty]
    [property: SavedSetting("EyeHome_LeftCameraDisplayedIndex", "")]
    private string _leftCameraDisplayedAddress;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_LeftCameraIndex", "")]
    private string _leftCameraAddress;

    [ObservableProperty]
    private Rect _leftOverlayRectangle;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_FlipLeftEyeXAxis", false)]
    private bool _flipLeftEyeXAxis;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_FlipLeftEyeYAxis", false)]
    private bool _flipLeftEyeYAxis;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_LeftEyeRotation", 0f)]
    private float _leftEyeRotation;

    // Right eye properties
    [ObservableProperty]
    [property: SavedSetting("EyeHome_RightCameraDisplayedIndex", "")]
    private string _rightCameraDisplayedAddress;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_RightCameraIndex", "")]
    private string _rightCameraAddress;

    [ObservableProperty]
    private Rect _rightOverlayRectangle;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_FlipRightEyeXAxis", false)]
    private bool _flipRightEyeXAxis;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_FlipRightEyeYAxis", false)]
    private bool _flipRightEyeYAxis;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_RightEyeRotation", 0f)]
    private float _rightEyeRotation;

    // Face properties
    [ObservableProperty]
    [property: SavedSetting("EyeHome_FaceCameraDisplayedIndex", "")]
    private string _faceCameraDisplayedAddress;

    [ObservableProperty]
    [property: SavedSetting("EyeHome_FaceCameraIndex", "")]
    private string _faceCameraAddress;

    [ObservableProperty]
    [property: SavedSetting("Face_CameraROI")]
    private Rect _faceOverlayRectangle;

    [ObservableProperty]
    [property: SavedSetting("Face_FlipXAxis", false)]
    private bool _flipFaceXAxis;

    [ObservableProperty]
    [property: SavedSetting("Face_FlipYAxis", false)]
    private bool _flipFaceYAxis;

    [ObservableProperty]
    [property: SavedSetting("Face_Rotation", 0f)]
    private float _faceRotation;

    public IOscTarget OscTarget { get; }
    private OscRecvService OscRecvService { get; }
    private OscSendService OscSendService { get; }
    private ILocalSettingsService LocalSettingsService { get; }

    private int _messagesRecvd;
    [ObservableProperty] private string _messagesInPerSecCount;

    private int _messagesSent;
    [ObservableProperty] private string _messagesOutPerSecCount;

    private readonly DispatcherTimer _msgCounterTimer;

    public HomePageViewModel()
    {
        OscTarget = Ioc.Default.GetService<IOscTarget>()!;
        OscRecvService = Ioc.Default.GetService<OscRecvService>()!;
        OscSendService = Ioc.Default.GetService<OscSendService>()!;
        LocalSettingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        LocalSettingsService.Load(this);

        MessagesInPerSecCount = "0";
        MessagesOutPerSecCount = "0";
        OscRecvService.OnMessageReceived += MessageReceived;
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

        PropertyChanged += OnPropertyChangedEventHandler;
    }

    private void OnPropertyChangedEventHandler(object? o, PropertyChangedEventArgs args)
    {
        string propertyName = args.PropertyName!;

        if (propertyName is "MessagesOutPerSecCount" or "MessagesInPerSecCount")
            return;

        // Handle camera address properties
        HandleCameraAddressChange(propertyName);

        // Save all other changes
        LocalSettingsService.Save(this);
    }

    private void HandleCameraAddressChange(string propertyName)
    {
        switch (propertyName)
        {
            case "LeftCameraDisplayedAddress":
                UpdateCameraAddress(LeftCameraDisplayedAddress, address => LeftCameraAddress = address);
                break;

            case "RightCameraDisplayedAddress":
                UpdateCameraAddress(RightCameraDisplayedAddress, address => RightCameraAddress = address);
                break;

            case "FaceCameraDisplayedAddress":
                UpdateCameraAddress(FaceCameraDisplayedAddress, address => FaceCameraAddress = address);
                break;
        }
    }

    private void UpdateCameraAddress(string displayedAddress, Action<string> updateAction)
    {
        if (DeviceEnumerator.Cameras.TryGetValue(displayedAddress, out var deviceAddress))
        {
            updateAction(deviceAddress);
        }
        else
        {
            updateAction(displayedAddress);
        }
    }

    private void MessageReceived(OscMessage msg) => _messagesRecvd++;

    private void MessageDispatched(int msgCount) => _messagesSent += msgCount;

    ~HomePageViewModel()
    {
        CleanupResources();
    }

    private void CleanupResources()
    {
        OscRecvService.OnMessageReceived -= MessageReceived;
        OscSendService.OnMessagesDispatched -= MessageDispatched;
        _msgCounterTimer.Stop();
    }
}
