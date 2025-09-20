using Newtonsoft.Json;
using System.Reflection;

namespace VRCFaceTracking.Baballonia;

public static class BabbleConfig
{
    public static Config Config { get; private set; } = null!;

    public static (bool, bool) GetSupportedFeatures()
    {
        var config = GetBabbleConfig();
        return (config.IsEyeSupported, config.IsFaceSupported);
    }

    public static Config GetBabbleConfig()
    {
        if (Config != null) return Config;

		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
		string path = Path.Combine(directoryName, "BabbleConfig.json");
		string value = File.ReadAllText(path);

        Config = JsonConvert.DeserializeObject<Config>(value)!;
        return Config;
    }
}
