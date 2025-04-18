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
        [ObservableProperty] [property: SavedSetting("CheekPuffLeftLower", 0f)] private float _cheekPuffLeftLower;
        [ObservableProperty] [property: SavedSetting("CheekPuffLeftUpper", 1f)] private float _cheekPuffLeftUpper;

        [ObservableProperty] [property: SavedSetting("CheekPuffRightLower", 0f)] private float _cheekPuffRightLower;
        [ObservableProperty] [property: SavedSetting("CheekPuffRightUpper", 1f)] private float _cheekPuffRightUpper;

        [ObservableProperty] [property: SavedSetting("CheekSuckLeftLower", 0f)] private float _cheekSuckLeftLower;
        [ObservableProperty] [property: SavedSetting("CheekSuckLeftUpper", 1f)] private float _cheekSuckLeftUpper;

        [ObservableProperty] [property: SavedSetting("CheekSuckRightLower", 0f)] private float _cheekSuckRightLower;
        [ObservableProperty] [property: SavedSetting("CheekSuckRightUpper", 1f)] private float _cheekSuckRightUpper;

        [ObservableProperty] [property: SavedSetting("JawOpenLower", 0f)] private float _jawOpenLower;
        [ObservableProperty] [property: SavedSetting("JawOpenUpper", 1f)] private float _jawOpenUpper;

        [ObservableProperty] [property: SavedSetting("JawForwardLower", 0f)] private float _jawForwardLower;
        [ObservableProperty] [property: SavedSetting("JawForwardUpper", 1f)] private float _jawForwardUpper;

        [ObservableProperty] [property: SavedSetting("JawLeftLower", 0f)] private float _jawLeftLower;
        [ObservableProperty] [property: SavedSetting("JawLeftUpper", 1f)] private float _jawLeftUpper;

        [ObservableProperty] [property: SavedSetting("JawRightLower", 0f)] private float _jawRightLower;
        [ObservableProperty] [property: SavedSetting("JawRightUpper", 1f)] private float _jawRightUpper;

        [ObservableProperty] [property: SavedSetting("NoseSneerLeftLower", 0f)] private float _noseSneerLeftLower;
        [ObservableProperty] [property: SavedSetting("NoseSneerLeftUpper", 1f)] private float _noseSneerLeftUpper;

        [ObservableProperty] [property: SavedSetting("NoseSneerRightLower", 0f)] private float _noseSneerRightLower;
        [ObservableProperty] [property: SavedSetting("NoseSneerRightUpper", 1f)] private float _noseSneerRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthFunnelLower", 0f)] private float _mouthFunnelLower;
        [ObservableProperty] [property: SavedSetting("MouthFunnelUpper", 1f)] private float _mouthFunnelUpper;

        [ObservableProperty] [property: SavedSetting("MouthPuckerLower", 0f)] private float _mouthPuckerLower;
        [ObservableProperty] [property: SavedSetting("MouthPuckerUpper", 1f)] private float _mouthPuckerUpper;

        [ObservableProperty] [property: SavedSetting("MouthLeftLower", 0f)] private float _mouthLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthLeftUpper", 1f)] private float _mouthLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthRightLower", 0f)] private float _mouthRightLower;
        [ObservableProperty] [property: SavedSetting("MouthRightUpper", 1f)] private float _mouthRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthRollUpperLower", 0f)] private float _mouthRollUpperLower;
        [ObservableProperty] [property: SavedSetting("MouthRollUpperUpper", 1f)] private float _mouthRollUpperUpper;

        [ObservableProperty] [property: SavedSetting("MouthRollLowerLower", 0f)] private float _mouthRollLowerLower;
        [ObservableProperty] [property: SavedSetting("MouthRollLowerUpper", 1f)] private float _mouthRollLowerUpper;

        [ObservableProperty] [property: SavedSetting("MouthShrugUpperLower", 0f)] private float _mouthShrugUpperLower;
        [ObservableProperty] [property: SavedSetting("MouthShrugUpperUpper", 1f)] private float _mouthShrugUpperUpper;

        [ObservableProperty] [property: SavedSetting("MouthShrugLowerLower", 0f)] private float _mouthShrugLowerLower;
        [ObservableProperty] [property: SavedSetting("MouthShrugLowerUpper", 1f)] private float _mouthShrugLowerUpper;

        [ObservableProperty] [property: SavedSetting("MouthCloseLower", 0f)] private float _mouthCloseLower;
        [ObservableProperty] [property: SavedSetting("MouthCloseUpper", 1f)] private float _mouthCloseUpper;

        [ObservableProperty] [property: SavedSetting("MouthSmileLeftLower", 0f)] private float _mouthSmileLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthSmileLeftUpper", 1f)] private float _mouthSmileLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthSmileRightLower", 0f)] private float _mouthSmileRightLower;
        [ObservableProperty] [property: SavedSetting("MouthSmileRightUpper", 1f)] private float _mouthSmileRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthFrownLeftLower", 0f)] private float _mouthFrownLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthFrownLeftUpper", 1f)] private float _mouthFrownLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthFrownRightLower", 0f)] private float _mouthFrownRightLower;
        [ObservableProperty] [property: SavedSetting("MouthFrownRightUpper", 1f)] private float _mouthFrownRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthDimpleLeftLower", 0f)] private float _mouthDimpleLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthDimpleLeftUpper", 1f)] private float _mouthDimpleLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthDimpleRightLower", 0f)] private float _mouthDimpleRightLower;
        [ObservableProperty] [property: SavedSetting("MouthDimpleRightUpper", 1f)] private float _mouthDimpleRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthUpperUpLeftLower", 0f)] private float _mouthUpperUpLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthUpperUpLeftUpper", 1f)] private float _mouthUpperUpLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthUpperUpRightLower", 0f)] private float _mouthUpperUpRightLower;
        [ObservableProperty] [property: SavedSetting("MouthUpperUpRightUpper", 1f)] private float _mouthUpperUpRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthLowerDownLeftLower", 0f)] private float _mouthLowerDownLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthLowerDownLeftUpper", 1f)] private float _mouthLowerDownLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthLowerDownRightLower", 0f)] private float _mouthLowerDownRightLower;
        [ObservableProperty] [property: SavedSetting("MouthLowerDownRightUpper", 1f)] private float _mouthLowerDownRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthPressLeftLower", 0f)] private float _mouthPressLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthPressLeftUpper", 1f)] private float _mouthPressLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthPressRightLower", 0f)] private float _mouthPressRightLower;
        [ObservableProperty] [property: SavedSetting("MouthPressRightUpper", 1f)] private float _mouthPressRightUpper;

        [ObservableProperty] [property: SavedSetting("MouthStretchLeftLower", 0f)] private float _mouthStretchLeftLower;
        [ObservableProperty] [property: SavedSetting("MouthStretchLeftUpper", 1f)] private float _mouthStretchLeftUpper;

        [ObservableProperty] [property: SavedSetting("MouthStretchRightLower", 0f)] private float _mouthStretchRightLower;
        [ObservableProperty] [property: SavedSetting("MouthStretchRightUpper", 1f)] private float _mouthStretchRightUpper;

        [ObservableProperty] [property: SavedSetting("TongueOutLower", 0f)] private float _tongueOutLower;
        [ObservableProperty] [property: SavedSetting("TongueOutUpper", 1f)] private float _tongueOutUpper;

        [ObservableProperty] [property: SavedSetting("TongueUpLower", 0f)] private float _tongueUpLower;
        [ObservableProperty] [property: SavedSetting("TongueUpUpper", 1f)] private float _tongueUpUpper;

        [ObservableProperty] [property: SavedSetting("TongueDownLower", 0f)] private float _tongueDownLower;
        [ObservableProperty] [property: SavedSetting("TongueDownUpper", 1f)] private float _tongueDownUpper;

        [ObservableProperty] [property: SavedSetting("TongueLeftLower", 0f)] private float _tongueLeftLower;
        [ObservableProperty] [property: SavedSetting("TongueLeftUpper", 1f)] private float _tongueLeftUpper;

        [ObservableProperty] [property: SavedSetting("TongueRightLower", 0f)] private float _tongueRightLower;
        [ObservableProperty] [property: SavedSetting("TongueRightUpper", 1f)] private float _tongueRightUpper;

        [ObservableProperty] [property: SavedSetting("TongueRollLower", 0f)] private float _tongueRollLower;
        [ObservableProperty] [property: SavedSetting("TongueRollUpper", 1f)] private float _tongueRollUpper;

        [ObservableProperty] [property: SavedSetting("TongueBendDownLower", 0f)] private float _tongueBendDownLower;
        [ObservableProperty] [property: SavedSetting("TongueBendDownUpper", 1f)] private float _tongueBendDownUpper;

        [ObservableProperty] [property: SavedSetting("TongueCurlUpLower", 0f)] private float _tongueCurlUpLower;
        [ObservableProperty] [property: SavedSetting("TongueCurlUpUpper", 1f)] private float _tongueCurlUpUpper;

        [ObservableProperty] [property: SavedSetting("TongueSquishLower", 0f)] private float _tongueSquishLower;
        [ObservableProperty] [property: SavedSetting("TongueSquishUpper", 1f)] private float _tongueSquishUpper;

        [ObservableProperty] [property: SavedSetting("TongueFlatLower", 0f)] private float _tongueFlatLower;
        [ObservableProperty] [property: SavedSetting("TongueFlatUpper", 1f)] private float _tongueFlatUpper;

        [ObservableProperty] [property: SavedSetting("TongueTwistLeftLower", 0f)] private float _tongueTwistLeftLower;
        [ObservableProperty] [property: SavedSetting("TongueTwistLeftUpper", 1f)] private float _tongueTwistLeftUpper;

        [ObservableProperty] [property: SavedSetting("TongueTwistRightLower", 0f)] private float _tongueTwistRightLower;
        [ObservableProperty] [property: SavedSetting("TongueTwistRightUpper", 1f)] private float _tongueTwistRightUpper;

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

        public Dictionary<string, (float Lower, float Upper)> GetCalibrationValues()
        {
            return new Dictionary<string, (float, float)>
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
