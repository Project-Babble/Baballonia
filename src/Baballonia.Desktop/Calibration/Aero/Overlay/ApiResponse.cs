using Newtonsoft.Json;

namespace Baballonia.Desktop.Calibration.Aero.Overlay;

public class ApiResponse
{
    [JsonProperty("result")]
    public required string Result { get; set; }

    [JsonProperty("message")]
    public required string Message { get; set; }
}
