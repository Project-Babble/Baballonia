using Newtonsoft.Json;
using System.Reflection;

namespace VRCFaceTracking.Baballonia;

public static class BabbleConfig
{
    public static Config Config { get; private set; } = null!;

    private static readonly object SaveLock = new();

    private static string ConfigPath =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "BabbleConfig.json");

    public static Config GetBabbleConfig()
    {
        if (Config != null) return Config;

        string value = File.ReadAllText(ConfigPath);

        Config = JsonConvert.DeserializeObject<Config>(value)!;
        return Config;
    }

    /// <summary>Writes the config back to BabbleConfig.json, through a temporary file so a crash never leaves half a file.</summary>
    public static void SaveBabbleConfig()
    {
        lock (SaveLock)
        {
            string temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(GetBabbleConfig(), Formatting.Indented));
            File.Move(temp, ConfigPath, overwrite: true);
        }
    }
}
