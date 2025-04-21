using Newtonsoft.Json;
using System.Reflection;

namespace VRCFaceTracking.Babble;

public static class BabbleConfig
{
	public static Config GetBabbleConfig()
	{
		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
		string path = Path.Combine(directoryName, "BabbleEyeConfig.json");
		string value = File.ReadAllText(path);
		return JsonConvert.DeserializeObject<Config>(value)!;
	}
}
