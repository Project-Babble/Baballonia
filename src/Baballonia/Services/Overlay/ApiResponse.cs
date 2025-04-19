using Newtonsoft.Json;

namespace AvaloniaMiaDev.Services.Overlay;

public class ApiResponse
{
    [JsonProperty("result")]
    public string Result { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}
