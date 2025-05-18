namespace Baballonia.Services.Firmware;

// Payload structure classes
public class WifiData
{
    public string ssid { get; set; }
    public string password { get; set; }
}

public class MdnsData
{
    public const string DefaultHostName = "openiris.local";
    public string hostname { get; set; }
}

public class Command
{
    public string command { get; set; }
    public object data { get; set; }
}

public class Payload
{
    public Command[] commands { get; set; }
}
