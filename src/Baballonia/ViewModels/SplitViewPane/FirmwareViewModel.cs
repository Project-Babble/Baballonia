using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Baballonia.ViewModels.SplitViewPane;

public partial class FirmwareViewModel : ViewModelBase
{
    // This entire thing is a hack to hold us over

    [RelayCommand]
    private async Task LaunchOpenIrisUtil()
    {
        using var process = new Process();
        process.StartInfo.FileName = Path.Combine("OpenIris", "openiris_setup.exe");
        process.StartInfo.CreateNoWindow = false;
        process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
        process.Start();
        await process.WaitForExitAsync();
    }
}
