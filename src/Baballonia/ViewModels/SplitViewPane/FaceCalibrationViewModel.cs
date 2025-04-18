using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using AvaloniaMiaDev.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using AvaloniaMiaDev.Models;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace AvaloniaMiaDev.ViewModels.SplitViewPane
{
    public partial class FaceCalibrationViewModel : ViewModelBase
    {
        [ObservableProperty] [property: SavedSetting("CheekPuffLeftLower", 0)] private double _cheekPuffLeftLower;
        [ObservableProperty] [property: SavedSetting("CheekPuffLeftUpper", 1)] private double _cheekPuffLeftUpper;

        [ObservableProperty] [property: SavedSetting("CheekPuffRightLower", 0)] private double _cheekPuffRightLower;
        [ObservableProperty] [property: SavedSetting("CheekPuffRightUpper", 1)] private double _cheekPuffRightUpper;

        [ObservableProperty] [property: SavedSetting("CheekSuckLeftLower", 0)] private double _cheekSuckLeftLower;
        [ObservableProperty] [property: SavedSetting("CheekSuckLeftUpper", 1)] private double _cheekSuckLeftUpper;

        [ObservableProperty] [property: SavedSetting("CheekSuckRightLower", 0)] private double _cheekSuckRightLower;
        [ObservableProperty] [property: SavedSetting("CheekSuckRightUpper", 1)] private double _cheekSuckRightUpper;

        [ObservableProperty] [property: SavedSetting("JawOpenLower", 0)] private double _jawOpenLower;
        [ObservableProperty] [property: SavedSetting("JawOpenUpper", 1)] private double _jawOpenUpper;

        [ObservableProperty] [property: SavedSetting("JawForwardLower", 0)] private double _jawForwardLower;
        [ObservableProperty] [property: SavedSetting("JawForwardUpper", 1)] private double _jawForwardUpper;

        [ObservableProperty] [property: SavedSetting("JawLeftLower", 0)] private double _jawLeftLower;
        [ObservableProperty] [property: SavedSetting("JawLeftUpper", 1)] private double _jawLeftUpper;

        [ObservableProperty] [property: SavedSetting("JawRightLower", 0)] private double _jawRightLower;
        [ObservableProperty] [property: SavedSetting("JawRightUpper", 1)] private double _jawRightUpper;

        [ObservableProperty] [property: SavedSetting("NoseSneerLeftLower", 0)] private double _noseSneerLeftLower;
        [ObservableProperty] [property: SavedSetting("NoseSneerLeftUpper", 1)] private double _noseSneerLeftUpper;

        [ObservableProperty] [property: SavedSetting("NoseSneerRightLower", 0)] private double _noseSneerRightLower;
        [ObservableProperty] [property: SavedSetting("NoseSneerRightUpper", 1)] private double _noseSneerRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthFunnelLower", 0)] private double _mouthFunnelLower;
        [ObservableProperty] [property: SavedSetting("MouthFunnelUpper", 1)] private double _mouthFunnelUpper;

        [ObservableProperty] [property: SavedSetting("MouthPuckerLower", 0)] private double _mouthPuckerLower;
        [ObservableProperty] [property: SavedSetting("MouthPuckerUpper", 1)] private double _mouthPuckerUpper;

        [ObservableProperty] [property: SavedSetting("MouthLeftLower", 0)] private double _mouthLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthLeftUpper", 1)] private double _mouthLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthRightLower", 0)] private double _mouthRightLower;
        [ObservableProperty] [property: SavedSetting("MouthRightUpper", 1)] private double _mouthRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthRollUpperLower", 0)] private double _mouthRollUpperLower;
        [ObservableProperty] [property: SavedSetting("MouthRollUpperUpper", 1)] private double _mouthRollUpperUpper;

        [ObservableProperty] [property: SavedSetting("MouthRollLowerLower", 0)] private double _mouthRollLowerLower;
        [ObservableProperty] [property: SavedSetting("MouthRollLowerUpper", 1)] private double _mouthRollLowerUpper;

        [ObservableProperty] [property: SavedSetting("MouthShrugUpperLower", 0)] private double _mouthShrugUpperLower;
        [ObservableProperty] [property: SavedSetting("MouthShrugUpperUpper", 1)] private double _mouthShrugUpperUpper;

        [ObservableProperty] [property: SavedSetting("MouthShrugLowerLower", 0)] private double _mouthShrugLowerLower;
        [ObservableProperty] [property: SavedSetting("MouthShrugLowerUpper", 1)] private double _mouthShrugLowerUpper;

        [ObservableProperty] [property: SavedSetting("MouthCloseLower", 0)] private double _mouthCloseLower;
        [ObservableProperty] [property: SavedSetting("MouthCloseUpper", 1)] private double _mouthCloseUpper;

        [ObservableProperty] [property: SavedSetting("MouthSmileLeftLower", 0)] private double _mouthSmileLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthSmileLeftUpper", 1)] private double _mouthSmileLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthSmileRightLower", 0)] private double _mouthSmileRightLower;
        [ObservableProperty] [property: SavedSetting("MouthSmileRightUpper", 1)] private double _mouthSmileRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthFrownLeftLower", 0)] private double _mouthFrownLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthFrownLeftUpper", 1)] private double _mouthFrownLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthFrownRightLower", 0)] private double _mouthFrownRightLower;
        [ObservableProperty] [property: SavedSetting("MouthFrownRightUpper", 1)] private double _mouthFrownRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthDimpleLeftLower", 0)] private double _mouthDimpleLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthDimpleLeftUpper", 1)] private double _mouthDimpleLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthDimpleRightLower", 0)] private double _mouthDimpleRightLower;
        [ObservableProperty] [property: SavedSetting("MouthDimpleRightUpper", 1)] private double _mouthDimpleRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthUpperUpLeftLower", 0)] private double _mouthUpperUpLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthUpperUpLeftUpper", 1)] private double _mouthUpperUpLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthUpperUpRightLower", 0)] private double _mouthUpperUpRightLower;
        [ObservableProperty] [property: SavedSetting("MouthUpperUpRightUpper", 1)] private double _mouthUpperUpRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthLowerDownLeftLower", 0)] private double _mouthLowerDownLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthLowerDownLeftUpper", 1)] private double _mouthLowerDownLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthLowerDownRightLower", 0)] private double _mouthLowerDownRightLower;
        [ObservableProperty] [property: SavedSetting("MouthLowerDownRightUpper", 1)] private double _mouthLowerDownRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthPressLeftLower", 0)] private double _mouthPressLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthPressLeftUpper", 1)] private double _mouthPressLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthPressRightLower", 0)] private double _mouthPressRightLower;
        [ObservableProperty] [property: SavedSetting("MouthPressRightUpper", 1)] private double _mouthPressRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthStretchLeftLower", 0)] private double _mouthStretchLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthStretchLeftUpper", 1)] private double _mouthStretchLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthStretchRightLower", 0)] private double _mouthStretchRightLower;
        [ObservableProperty] [property: SavedSetting("MouthStretchRightUpper", 1)] private double _mouthStretchRightUpper;

        [ObservableProperty] [property: SavedSetting("TongueOutLower", 0)] private double _tongueOutLower;
        [ObservableProperty] [property: SavedSetting("TongueOutUpper", 1)] private double _tongueOutUpper;

        [ObservableProperty] [property: SavedSetting("TongueUpLower", 0)] private double _tongueUpLower;
        [ObservableProperty] [property: SavedSetting("TongueUpUpper", 1)] private double _tongueUpUpper;

        [ObservableProperty] [property: SavedSetting("TongueDownLower", 0)] private double _tongueDownLower;
        [ObservableProperty] [property: SavedSetting("TongueDownUpper", 1)] private double _tongueDownUpper;

        [ObservableProperty] [property: SavedSetting("TongueLeftLower", 0)] private double _tongueLeftLower;
        [ObservableProperty] [property: SavedSetting("TongueLeftUpper", 1)] private double _tongueLeftUpper;

        [ObservableProperty] [property: SavedSetting("TongueRightLower", 0)] private double _tongueRightLower;
        [ObservableProperty] [property: SavedSetting("TongueRightUpper", 1)] private double _tongueRightUpper;

        [ObservableProperty] [property: SavedSetting("TongueRollLower", 0)] private double _tongueRollLower;
        [ObservableProperty] [property: SavedSetting("TongueRollUpper", 1)] private double _tongueRollUpper;

        [ObservableProperty] [property: SavedSetting("TongueBendDownLower", 0)] private double _tongueBendDownLower;
        [ObservableProperty] [property: SavedSetting("TongueBendDownUpper", 1)] private double _tongueBendDownUpper;

        [ObservableProperty] [property: SavedSetting("TongueCurlUpLower", 0)] private double _tongueCurlUpLower;
        [ObservableProperty] [property: SavedSetting("TongueCurlUpUpper", 1)] private double _tongueCurlUpUpper;

        [ObservableProperty] [property: SavedSetting("TongueSquishLower", 0)] private double _tongueSquishLower;
        [ObservableProperty] [property: SavedSetting("TongueSquishUpper", 1)] private double _tongueSquishUpper;

        [ObservableProperty] [property: SavedSetting("TongueFlatLower", 0)] private double _tongueFlatLower;
        [ObservableProperty] [property: SavedSetting("TongueFlatUpper", 1)] private double _tongueFlatUpper;

        [ObservableProperty] [property: SavedSetting("TongueTwistLeftLower", 0)] private double _tongueTwistLeftLower;
        [ObservableProperty] [property: SavedSetting("TongueTwistLeftUpper", 1)] private double _tongueTwistLeftUpper;

        [ObservableProperty] [property: SavedSetting("TongueTwistRightLower", 0)] private double _tongueTwistRightLower;
        [ObservableProperty] [property: SavedSetting("TongueTwistRightUpper", 1)] private double _tongueTwistRightUpper;

        private ILocalSettingsService _settingsService { get; }

        public FaceCalibrationViewModel()
        {
            _settingsService = Ioc.Default.GetService<ILocalSettingsService>()!;
            _settingsService.Load(this);

            PropertyChanged += (_, _) =>
            {
                _settingsService.Save(this);
            };
        }

        public Dictionary<string, (double Lower, double Upper)> GetCalibrationValues()
        {
            return new Dictionary<string, (double, double)>
            {
                { "/cheekPuffLeft", (CheekPuffLeftLower, CheekPuffLeftUpper) },
                { "/cheekPuffRight", (CheekPuffRightLower, CheekPuffRightUpper) },
                { "/cheekSuckLeft", (CheekSuckLeftLower, CheekSuckLeftUpper) },
                { "/cheekSuckRight", (CheekSuckRightLower, CheekSuckRightUpper) },
                { "/jawOpen", (JawOpenLower, JawOpenUpper) },
                { "/jawForward", (JawForwardLower, JawForwardUpper) },
                { "/jawLeft", (JawLeftLower, JawLeftUpper) },
                { "/jawRight", (JawRightLower, JawRightUpper) },
                { "/noseSneerLeft", (NoseSneerLeftLower, NoseSneerLeftUpper) },
                { "/noseSneerRight", (NoseSneerRightLower, NoseSneerRightUpper) },
                { "/mouthFunnel", (MouthFunnelLower, MouthFunnelUpper) },
                { "/mouthPucker", (MouthPuckerLower, MouthPuckerUpper) },
                { "/mouthLeft", (MouthLeftLower, MouthLeftUpper) },
                { "/mouthRight", (MouthRightLower, MouthRightUpper) },
                { "/mouthRollUpper", (MouthRollUpperLower, MouthRollUpperUpper) },
                { "/mouthRollLower", (MouthRollLowerLower, MouthRollLowerUpper) },
                { "/mouthShrugUpper", (MouthShrugUpperLower, MouthShrugUpperUpper) },
                { "/mouthShrugLower", (MouthShrugLowerLower, MouthShrugLowerUpper) },
                { "/mouthClose", (MouthCloseLower, MouthCloseUpper) },
                { "/mouthSmileLeft", (MouthSmileLeftLower, MouthSmileLeftUpper) },
                { "/mouthSmileRight", (MouthSmileRightLower, MouthSmileRightUpper) },
                { "/mouthFrownLeft", (MouthFrownLeftLower, MouthFrownLeftUpper) },
                { "/mouthFrownRight", (MouthFrownRightLower, MouthFrownRightUpper) },
                { "/mouthDimpleLeft", (MouthDimpleLeftLower, MouthDimpleLeftUpper) },
                { "/mouthDimpleRight", (MouthDimpleRightLower, MouthDimpleRightUpper) },
                { "/mouthUpperUpLeft", (MouthUpperUpLeftLower, MouthUpperUpLeftUpper) },
                { "/mouthUpperUpRight", (MouthUpperUpRightLower, MouthUpperUpRightUpper) },
                { "/mouthLowerDownLeft", (MouthLowerDownLeftLower, MouthLowerDownLeftUpper) },
                { "/mouthLowerDownRight", (MouthLowerDownRightLower, MouthLowerDownRightUpper) },
                { "/mouthPressLeft", (MouthPressLeftLower, MouthPressLeftUpper) },
                { "/mouthPressRight", (MouthPressRightLower, MouthPressRightUpper) },
                { "/mouthStretchLeft", (MouthStretchLeftLower, MouthStretchLeftUpper) },
                { "/mouthStretchRight", (MouthStretchRightLower, MouthStretchRightUpper) },
                { "/tongueOut", (TongueOutLower, TongueOutUpper) },
                { "/tongueUp", (TongueUpLower, TongueUpUpper) },
                { "/tongueDown", (TongueDownLower, TongueDownUpper) },
                { "/tongueLeft", (TongueLeftLower, TongueLeftUpper) },
                { "/tongueRight", (TongueRightLower, TongueRightUpper) },
                { "/tongueRoll", (TongueRollLower, TongueRollUpper) },
                { "/tongueBendDown", (TongueBendDownLower, TongueBendDownUpper) },
                { "/tongueCurlUp", (TongueCurlUpLower, TongueCurlUpUpper) },
                { "/tongueSquish", (TongueSquishLower, TongueSquishUpper) },
                { "/tongueFlat", (TongueFlatLower, TongueFlatUpper) },
                { "/tongueTwistLeft", (TongueTwistLeftLower, TongueTwistLeftUpper) },
                { "/tongueTwistRight", (TongueTwistRightLower, TongueTwistRightUpper) }
            };
        }

        public void ResetCalibrationValues(Selection selection)
        {
            switch (selection)
            {
                case Selection.Min:
                    CheekPuffLeftLower = 0f;
                    CheekPuffRightLower = 0f;
                    CheekSuckLeftLower = 0f;
                    CheekSuckRightLower = 0f;
                    JawOpenLower = 0f;
                    JawForwardLower = 0f;
                    JawLeftLower = 0f;
                    JawRightLower = 0f;
                    NoseSneerLeftLower = 0f;
                    NoseSneerRightLower = 0f;
                    MouthFunnelLower = 0f;
                    MouthPuckerLower = 0f;
                    MouthLeftLower = 0f;
                    MouthRightLower = 0f;
                    MouthRollUpperLower = 0f;
                    MouthRollLowerLower = 0f;
                    MouthShrugUpperLower = 0f;
                    MouthShrugLowerLower = 0f;
                    MouthCloseLower = 0f;
                    TongueOutLower = 0f;
                    TongueUpLower = 0f;
                    TongueDownLower = 0f;
                    TongueLeftLower = 0f;
                    TongueRightLower = 0f;
                    TongueRollLower = 0f;
                    TongueBendDownLower = 0f;
                    TongueCurlUpLower = 0f;
                    TongueSquishLower = 0f;
                    TongueFlatLower = 0f;
                    TongueTwistLeftLower = 0f;
                    TongueTwistRightLower = 0f;
                    break;
                case Selection.Max:
                    CheekPuffLeftUpper = 1f;
                    CheekPuffRightUpper = 1f;
                    CheekSuckLeftUpper = 1f;
                    CheekSuckRightUpper = 1f;
                    JawOpenUpper = 1f;
                    JawForwardUpper = 1f;
                    JawLeftUpper = 1f;
                    JawRightUpper = 1f;
                    NoseSneerLeftUpper = 1f;
                    NoseSneerRightUpper = 1f;
                    MouthFunnelUpper = 1f;
                    MouthPuckerUpper = 1f;
                    MouthLeftUpper = 1f;
                    MouthRightUpper = 1f;
                    MouthRollUpperUpper = 1f;
                    MouthRollUpperUpper = 1f;
                    MouthShrugUpperUpper = 1f;
                    MouthShrugUpperUpper = 1f;
                    MouthCloseUpper = 1f;
                    TongueOutUpper = 1f;
                    TongueUpUpper = 1f;
                    TongueDownUpper = 1f;
                    TongueLeftUpper = 1f;
                    TongueRightUpper = 1f;
                    TongueRollUpper = 1f;
                    TongueBendDownUpper = 1f;
                    TongueCurlUpUpper = 1f;
                    TongueSquishUpper = 1f;
                    TongueFlatUpper = 1f;
                    TongueTwistLeftUpper = 1f;
                    TongueTwistRightUpper = 1f;
                    break;
            }
        }

        public enum Selection
        {
            Min,
            Max
        }
    }
}
