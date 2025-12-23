using Newtonsoft.Json;

namespace Baballonia.Desktop.Calibration.Aero.Overlay;

public class VrCalibrationStatus
{
    [JsonProperty("running")]
    public required string Running { get; set; }

    [JsonProperty("recording")]
    public required string Recording { get; set; }

    [JsonProperty("calibrationComplete")]
    public required string CalibrationComplete { get; set; }

    [JsonProperty("isTrained")]
    public required string Trained { get; set; }

    [JsonProperty("currentIndex")]
    public int CurrentIndex { get; set; }

    [JsonProperty("maxIndex")]
    public int MaxIndex { get; set; }

    public bool IsRunning => Running == "1";
    public bool IsRecording => Recording == "1";
    public bool IsTrained => Trained == "1";
    public bool IsCalibrationComplete => CalibrationComplete == "1";
    public double Progress => MaxIndex > 0 ? (double)CurrentIndex / MaxIndex : 0;
}
