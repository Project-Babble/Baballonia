using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EspDotNet;
using EspDotNet.Tools.Firmware;

namespace Baballonia.Services;

// Shamelessy yoinked from https://github.com/KooleControls/ESPTool/blob/main/ESPTool/Example.cs
public class FirmwareService
{
    public event Action OnFirmwareUpdateStart;
    public event Action<float> OnFirmwareUpdateProgress;
    public event Action OnFirmwareUpdateComplete;

    private static readonly int BaudRate = OperatingSystem.IsMacOS() ? 115200 : 3000000; // Higher baud rate does not work on macOS

    public async Task UploadFirmwareAsync(string port, string pathToFirmware, CancellationToken token = default)
    {
        var toolbox = new ESPToolbox();
        var communicator = toolbox.CreateCommunicator();
        toolbox.OpenSerial(communicator, port, BaudRate);

        var loader = await toolbox.StartBootloaderAsync(communicator);
        var chipType = await toolbox.DetectChipTypeAsync(loader);

        var softloader = await toolbox.StartSoftloaderAsync(communicator, loader, chipType);
        await toolbox.ChangeBaudAsync(communicator, softloader, 921600);

        var uploadTool = toolbox.CreateUploadFlashDeflatedTool(softloader, chipType);
        var myFirmware = GetFirmware(pathToFirmware);
        var progress = new Progress<float>(p => OnFirmwareUpdateProgress?.Invoke(p));

        OnFirmwareUpdateStart?.Invoke();
        await toolbox.UploadFirmwareAsync(uploadTool, myFirmware, token, progress);
        await toolbox.ResetDeviceAsync(communicator);
        OnFirmwareUpdateComplete?.Invoke();
    }

    private IFirmwareProvider GetFirmware(string pathToFirmware)
    {
        return new FirmwareProvider(
            entryPoint: 0x00000000,
            segments:
            [
                new FirmwareSegmentProvider(0x00001000, File.ReadAllBytes(pathToFirmware)),
            ]
        );
    }
}
