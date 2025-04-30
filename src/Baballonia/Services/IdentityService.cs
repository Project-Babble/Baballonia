using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Baballonia.Contracts;
using Microsoft.Win32;

namespace Baballonia.Services;

public class IdentityService : IIdentityService
{
    public string GetUniqueUserId()
    {
        string systemIdString = GetSystemId();
        return ComputeSha256Hash(systemIdString);
    }

    private static string GetSystemId()
    {
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsSystemId();
        }
        else if (OperatingSystem.IsLinux())
        {
            return File.Exists("/etc/machine-id") ? File.ReadAllText("/etc/machine-id").Trim() : GetFallbackId();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return GetMacOsSystemId();
        }
        return GetFallbackId();
    }

    [SupportedOSPlatform("windows")]
    private static string GetWindowsSystemId()
    {
        try
        {
            using var registryKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return registryKey?.GetValue("MachineGuid")?.ToString() ?? GetFallbackId();
        }
        catch
        {
            return GetFallbackId();
        }
    }

    private static string GetMacOsSystemId()
    {
        try
        {
            string output = ExecuteCommand("ioreg -rd1 -c IOPlatformExpertDevice | awk '/IOPlatformUUID/ { print $3; }'");
            return output.Trim('"', '\n', '\r'); // Remove extra quotes and whitespace
        }
        catch
        {
            return GetFallbackId();
        }
    }

    private static string GetFallbackId()
    {
        return $"{Environment.MachineName}-{Environment.UserName}";
    }

    private static string ComputeSha256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }

    private static string ExecuteCommand(string command)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        process.Start();
        return process.StandardOutput.ReadToEnd();
    }
}
