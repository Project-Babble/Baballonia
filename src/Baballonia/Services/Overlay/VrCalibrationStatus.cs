using Newtonsoft.Json;

namespace Baballonia.Services.Overlay;

public class VrCalibrationStatus
{
    [JsonProperty("running")]
    public string Running { get; set; }

    [JsonProperty("recording")]
    public string Recording { get; set; }

    [JsonProperty("calibrationComplete")]
    public string CalibrationComplete { get; set; }

    [JsonProperty("isTrained")]
    public string Trained { get; set; }

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
