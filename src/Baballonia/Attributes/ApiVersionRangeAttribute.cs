using System;

namespace Baballonia.Attributes;


[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class ApiVersionRangeAttribute : Attribute
{
    public Version MinVersion { get; }
    public Version? MaxVersion { get; }

    public ApiVersionRangeAttribute(string minVersion)
    {
        MinVersion = new Version(minVersion);
    }

    public ApiVersionRangeAttribute(string minVersion, string maxVersion)
    {
        MinVersion = new Version(minVersion);
        MaxVersion = new Version(maxVersion);
    }

    public bool IsAllowed(Version version)
    {
        if (version < MinVersion) return false;
        if (MaxVersion != null && version > MaxVersion) return false;
        return true;
    }
}
