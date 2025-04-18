using System.Collections.ObjectModel;
using AvaloniaMiaDev.Contracts;
using AvaloniaMiaDev.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace AvaloniaMiaDev.ViewModels.SplitViewPane;

public partial class EyeCalibrationViewModel : ViewModelBase
{
    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_Algorithms")]
    private ObservableCollection<TrackingAlgorithm> _trackingAlgorithms;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_OuterEyeFalloff", false)]
    private float _outerEyeFalloff;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EyeDominantIndex", 0)]
    private int _eyeDominantIndex;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EyeDifferenceThreshold", 0.3f)]
    private int _eyeDifferenceThreshold;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_BlinkDetectionAlgorithmIndex", 0)]
    private int _blinkDetectionAlgorithmIndex;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_LEAPCalibrationSamples", 2000f)]
    private float _leapCalibrationSamples;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_IBOFilterSampleSize", 400f)]
    private float _iboFilterSampleSize;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_CalibrationSamples", 600f)]
    private float _calibrationSamples;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_IBOCloseThreshold", 0.3f)]
    private float _iboCloseThreshold;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EclipseBasedDilation?", false)]
    private bool _eclipseBasedDilation;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_SkipAutoRadius", false)]
    private bool _skipAutoRadius;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_LeftHSFRadius", 10f)]
    private float _leftHsfRadius;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_RightHSFRadius", 10f)]
    private float _rightHsfRadius;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_RansacThreshAdd", 11f)]
    private float _ransacThreshAdd;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_BlobThreshold", 65f)]
    private float _blobThreshold;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_MinBlobSize", 10f)]
    private float _minBlobSize;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_MaxBlobSize", 25f)]
    private float _maxBlobSize;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_RightEyeThresh", 80f)]
    private float _rightEyeThresh;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_LeftEyeThresh", 80f)]
    private float _leftEyeThresh;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EmulateEyeWiden", false)]
    private bool _emulateEyeWiden = false;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EyeWidenLower", 0f)]
    private float _eyeWidenLower = 0f;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EyeWidenUpper", 1f)]
    private float _eyeWidenUpper = 1f;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EmulateEyeSquint", false)]
    private bool _emulateEyeSquint = false;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EyeSquintLower", 0f)]
    private float _eyeSquintLower = 0f;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EyeSquintUpper", 1f)]
    private float _eyeSquintUpper = 1f;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EmulateEyebrows", false)]
    private bool _emulateEyebrows = false;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EyebrowsLower", 0f)]
    private float _eyeBrowsLower = 0f;

    [ObservableProperty]
    [property: SavedSetting("EyeCalibration_EyebrowsRaise", 1f)]
    private float _eyeBrowsUpper = 1f;

    public readonly CalibrationItem[] LeftEyeCalibrationItems =
    [
        new CalibrationItem { ShapeName = "/LeftEyeX", Min = -1, Max = 1 },
        new CalibrationItem { ShapeName = "/LeftEyeY", Min = -1, Max = 1 },
        new CalibrationItem { ShapeName = "/LeftExp1", Min = 0, Max = 1 },
        new CalibrationItem { ShapeName = "/LeftExp2", Min = 0, Max = 1 },
        new CalibrationItem { ShapeName = "/LeftExp3", Min = 0, Max = 1 },
        new CalibrationItem { ShapeName = "/LeftExp4", Min = 0, Max = 1 },
        new CalibrationItem { ShapeName = "/LeftExp5", Min = 0, Max = 1 },
    ];

    public readonly CalibrationItem[] RightEyeCalibrationItems =
    [
        new CalibrationItem { ShapeName = "/RightEyeX", Min = -1, Max = 1 },
        new CalibrationItem { ShapeName = "/RightEyeY", Min = -1, Max = 1 },
        new CalibrationItem { ShapeName = "/RightExp1", Min = 0, Max = 1 },
        new CalibrationItem { ShapeName = "/RightExp2", Min = 0, Max = 1 },
        new CalibrationItem { ShapeName = "/RightExp3", Min = 0, Max = 1 },
        new CalibrationItem { ShapeName = "/RightExp4", Min = 0, Max = 1 },
        new CalibrationItem { ShapeName = "/RightExp5", Min = 0, Max = 1 },
    ];

    private ILocalSettingsService _settingsService { get; }

    public EyeCalibrationViewModel()
    {
        _settingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
        _settingsService.Load(this);

        _trackingAlgorithms =
        [
            new TrackingAlgorithm(1, true, "ASHSFRAC", "Description"),
            new TrackingAlgorithm(2, false, "ASHSF", "Description"),
            new TrackingAlgorithm(3, false, "HSRAC", "Description"),
            new TrackingAlgorithm(4, false, "Haar Surround Feature", "Description"),
            new TrackingAlgorithm(5, false, "DADDY", "Description"),
            new TrackingAlgorithm(6, false, "RANSAC 3D", "Description"),
            new TrackingAlgorithm(7, false, "Blob", "Description"),
            new TrackingAlgorithm(8, false, "LEAP", "Description")
        ];

        PropertyChanged += (_, _) =>
        {
            _settingsService.Save(this);
        };
    }

    public void MoveModules(int currentIndex, int desiredIndex)
    {
        if (currentIndex < 0) return;

        if (desiredIndex >= 0 && desiredIndex < _trackingAlgorithms.Count)
            _trackingAlgorithms.Move(currentIndex, desiredIndex);

        RenumberModules();
    }

    private void RenumberModules()
    {
        for (int i = 0; i < _trackingAlgorithms.Count; i++)
        {
            _trackingAlgorithms[i].Order = i;
        }
    }
}
