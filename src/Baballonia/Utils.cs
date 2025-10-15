using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using Avalonia.Logging;

namespace Baballonia;

public static class Utils
{
    public const int EyeRawExpressions = 12;
    public const int FaceRawExpressions = 45;
    public const int FramesForEyeInference = 4;

    public static readonly bool IsSupportedDesktopOS = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    public const int MobileWidth = 900;

    private const string k_chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private static readonly MD5 hasher = MD5.Create();

    // Timer resolution helpers
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    public static extern uint TimeBeginPeriod(uint uMilliseconds);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage"), SuppressUnmanagedCodeSecurity]
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
    public static extern uint TimeEndPeriod(uint uMilliseconds);

    // Proc memory read helpers
    public const int ProcessVmRead = 0x0010;

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern bool ReadProcessMemory(int hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteFile(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint GetFileAttributes(string lpFileName);

    public static readonly bool HasAdmin = OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static readonly string UserAccessibleDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProjectBabble");

    public static readonly string PersistentDataDirectory = IsSupportedDesktopOS
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectBabble")
        : AppContext.BaseDirectory;

    public static readonly string ModelsDirectory = IsSupportedDesktopOS
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectBabble", "Models")
        : AppContext.BaseDirectory;

    public static readonly string ModelDataDirectory = IsSupportedDesktopOS
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectBabble", "ModelData")
        : AppContext.BaseDirectory;

    public static readonly string VrcftLibsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VRCFaceTracking",
        "CustomLibs");

    public static void ExtractEmbeddedResource(Assembly assembly, string resourceName, string file, bool overwrite = false)
    {
        // Extract the embedded model if it isn't already present
        if (File.Exists(file) && !overwrite) return;

        using var stm = assembly
            .GetManifestResourceStream(resourceName);

        using Stream outFile = File.Create(file);

        const int sz = 4096;
        var buf = new byte[sz];
        while (true)
        {
            if (stm == null) throw new FileNotFoundException(file);
            var nRead = stm.Read(buf, 0, sz);
            if (nRead < 1)
                break;
            outFile.Write(buf, 0, nRead);
        }
    }

    public static void OpenUrl(string URL)
    {
        try
        {
            Process.Start(URL);
        }
        catch
        {
            if (OperatingSystem.IsWindows())
            {
                var url = URL.Replace("&", "^&");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", URL);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", URL);
            }
        }
    }

    public static string RandomString(int length = 6)
    {
        return new string(Enumerable.Repeat(k_chars, length).Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }

    public static string GenerateMD5(string filepath)
    {
        // Credit to delta for this method https://github.com/XDelta/
        var stream = File.OpenRead(filepath);
        var hash = hasher.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "");
    }
}
