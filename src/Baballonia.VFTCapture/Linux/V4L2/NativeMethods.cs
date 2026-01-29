using System.Runtime.InteropServices;

namespace Baballonia.VFTCapture.Linux.V4L2;

internal static class NativeMethods {
    [DllImport("libv4l2", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern int v4l2_open(string file, int flags);

    [DllImport("libv4l2", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern int v4l2_close(int fd);

    [DllImport("libv4l2", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern int v4l2_ioctl(int fd, uint request, IntPtr arg);

    [DllImport("libv4l2", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern int v4l2_read(int fd, byte[] buffer, int size);

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
