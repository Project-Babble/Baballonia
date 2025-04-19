using System;
using System.Text;
using Hardware.Info;
using Microsoft.Extensions.Logging;

namespace AvaloniaMiaDev.Helpers;

public class DeviceHardwareLogger
{
    private readonly ILogger<DeviceHardwareLogger> _logger;
    private readonly HardwareInfo _hardwareInfo;

    public DeviceHardwareLogger(ILogger<DeviceHardwareLogger> logger)
    {
        _logger = logger;
        _hardwareInfo = new HardwareInfo();
    }

    public void LogHardwareInfo()
    {
        try
        {
            _hardwareInfo.RefreshAll();

            var sb = new StringBuilder();
            sb.AppendLine("=== Hardware Information ===");

            // CPU Information
            sb.AppendLine("\nCPU Information:");
            foreach (var cpu in _hardwareInfo.CpuList)
            {
                sb.AppendLine($"  Name: {cpu.Name}");
                sb.AppendLine($"  Number of Cores: {cpu.NumberOfCores}");
                sb.AppendLine($"  Number of Logical Processors: {cpu.NumberOfLogicalProcessors}");
                sb.AppendLine($"  Current Clock Speed: {cpu.CurrentClockSpeed} MHz");
            }

            // Memory Information
            sb.AppendLine("\nMemory Information:");
            foreach (var memory in _hardwareInfo.MemoryList)
            {
                sb.AppendLine($"  Manufacturer: {memory.Manufacturer}");
                sb.AppendLine($"  Part Number: {memory.PartNumber}");
                sb.AppendLine($"  Capacity: {memory.Capacity} bytes");
                sb.AppendLine($"  Speed: {memory.Speed} MHz");
            }

            // GPU Information
            sb.AppendLine("\nGPU Information:");
            foreach (var gpu in _hardwareInfo.VideoControllerList)
            {
                sb.AppendLine($"  Name: {gpu.Name}");
                sb.AppendLine($"  Driver Version: {gpu.DriverVersion}");
                sb.AppendLine($"  Video Processor: {gpu.VideoProcessor}");
                sb.AppendLine($"  Adapter RAM: {gpu.AdapterRAM} bytes");
            }

            // Operating System Information
            sb.AppendLine("\nOperating System Information:");
            sb.AppendLine($"  Name: {_hardwareInfo.OperatingSystem.Name}");
            sb.AppendLine($"  Version: {_hardwareInfo.OperatingSystem.VersionString}");

            _logger.LogInformation(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log hardware information");
        }
    }
}
