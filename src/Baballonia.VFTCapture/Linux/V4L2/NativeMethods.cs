using System.Runtime.InteropServices;

namespace Baballonia.VFTCapture.Linux.V4L2;

internal static class NativeMethods {
    // Device access goes straight to libc (open/close/ioctl) rather than libv4l2. Steam Linux
    // Runtimes ship only the versioned "libv4l2.so.0" with no unversioned "libv4l2" symlink, so the
    // old [DllImport("libv4l2")] failed to load there. libv4l2's only real value is on-the-fly format
    // emulation, which this path never uses — it selects hardware-native MJPEG/YUYV and does its own
    // mmap streaming — so libc is a drop-in with zero external dependency. Method names are unchanged
    // so the V4L2 device code is untouched.
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int sys_open([MarshalAs(UnmanagedType.LPUTF8Str)] string file, int flags, int mode);

    // O_CREAT is never passed, so the kernel ignores the variadic mode arg; pass 0.
    public static int v4l2_open(string file, int flags) => sys_open(file, flags, 0);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    public static extern int v4l2_close(int fd);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static extern int v4l2_ioctl(int fd, uint request, IntPtr arg);

    public static int v4l2_ioctl_safe<T>(int fd, uint request, ref T arg)
        where T : unmanaged
    {
        unsafe
        {
            fixed (T* p = &arg)
            {
                return v4l2_ioctl(fd, request, (IntPtr)p);
            }
        }
    }


    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr mmap(
        IntPtr addr,
        uint length,
        Prot prot,
        MapFlags flags,
        int fd,
        IntPtr offset);

    [DllImport("libc", SetLastError = true)]
    public static extern int munmap(IntPtr addr, uint length);

    [DllImport("libc", SetLastError = true)]
    public static extern int poll([In, Out] Data.pollfd[] fds, uint nfds, int timeout);
}
