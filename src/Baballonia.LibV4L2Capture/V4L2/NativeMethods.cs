using System.Runtime.InteropServices;

namespace Baballonia.LibV4L2Capture.V4L2;

internal static class NativeMethods {
    [DllImport("libv4l2.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern int v4l2_open(string file, int flags);

    [DllImport("libv4l2.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern int v4l2_close(int fd);

    [DllImport("libv4l2.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern int v4l2_ioctl(int fd, uint request, IntPtr arg);

    [DllImport("libv4l2.so", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern int v4l2_read(int fd, byte[] buffer, int size);


    [DllImport("libc.so.6", SetLastError = true)]
    public static extern IntPtr mmap(
        IntPtr addr,
        uint length,
        Prot prot,
        MapFlags flags,
        int fd,
        IntPtr offset);

    [DllImport("libc.so.6", SetLastError = true)]
    public static extern int munmap(IntPtr addr, uint length);
}
