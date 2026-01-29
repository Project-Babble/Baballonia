using System.Runtime.InteropServices;

namespace Baballonia.VFTCapture.Windows;

public partial class WindowsUsbCommunicator
{
    public enum ViveFacialTrackerError_t
    {
        VFT_OK = 0,
        VFT_COM_ERR = -1,
        VFT_TRACKER_NOT_FOUND = -2,
        VFT_FAIL = -3,
    };

    private const string UsbCommunicatorLibrary = "blluc";

    [LibraryImport(UsbCommunicatorLibrary)]
    public static partial int enableViveFacialTracker();
    [LibraryImport(UsbCommunicatorLibrary)]
    public static partial int disableViveFacialTracker();

    public static bool activate_tracker(int fd)
    {
        return enableViveFacialTracker() == (int)ViveFacialTrackerError_t.VFT_OK;
    }

    public static bool deactivate_tracker(int fd)
    {
        return disableViveFacialTracker() == (int)ViveFacialTrackerError_t.VFT_OK;
    }
}
