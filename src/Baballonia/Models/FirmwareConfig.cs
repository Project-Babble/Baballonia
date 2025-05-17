using System.Collections.Generic;

namespace Baballonia.Models;

public class Build
{
    public string chipFamily { get; set; }
    public List<Part> parts { get; set; }
}

public class Part
{
    public string path { get; set; }
    public int offset { get; set; }
}

public class FirmwareConfig
{
    public string name { get; set; }
    public string version { get; set; }
    public bool new_install_prompt_erase { get; set; }
    public List<Build> builds { get; set; }
}
