using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaMiaDev.Services.Inference.Captures
{
    public partial class ViveFacialTracker
    {
        public enum FileOpenFlags
        {
            O_RDONLY = 0x00,
            O_RDWR = 0x02,
            O_NONBLOCK = 0x800,
            O_SYNC = 0x101000
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct uvc_xu_control_query
        {
            public byte unit { get; set; }
            public byte selector { get; set; }
            public byte query { get; set; }
            public ushort size { get; set; }
            public IntPtr data { get; set; }
        }

        const int _XU_TASK_SET = 0x50;
        const int _XU_TASK_GET = 0x51;
        const int _XU_REG_SENSOR = 0xab;

        const int _UVC_SET_CUR = 0x01;
        const int _UVC_GET_CUR = 0x81;
        const int _UVC_GET_MIN = 0x82;
        const int _UVC_GET_MAX = 0x83;
        const int _UVC_GET_RES = 0x84;
        const int _UVC_GET_LEN = 0x85;
        const int _UVC_GET_INFO = 0x86;
        const int _UVC_GET_DEF = 0x87;
        public static int _UVCIOC_CTRL_QUERY = _IOWR<uvc_xu_control_query>('u', 0x21);

        private const string LibcLibrary = "libc";

        [LibraryImport(LibcLibrary, SetLastError = true)]
        public static partial int read(int fd, IntPtr buf, int count);
        [LibraryImport(LibcLibrary, SetLastError = true)]
        public static partial int open([MarshalAs(UnmanagedType.LPStr)] string pathname, FileOpenFlags flags);
        [LibraryImport(LibcLibrary)]
        internal static partial int close(int fd);
        [LibraryImport(LibcLibrary, SetLastError = true)]
        public static partial int ioctl(int fd, uint request, IntPtr argp);

        [LibraryImport(LibcLibrary, SetLastError = true)]
        public static partial int ioctl(int fd, int request, IntPtr argp);

        [LibraryImport(LibcLibrary, SetLastError = true)]
        internal static partial int ioctl(int fd, uint request, ulong argp);
        [LibraryImport(LibcLibrary, SetLastError = true)]
        public static partial int write(int fd, IntPtr buf, int count);

        const int _IOC_NRBITS = 8;
        const int _IOC_TYPEBITS = 8;
        const int _IOC_SIZEBITS = 14;
        const int _IOC_DIRBITS = 2;

        const int _IOC_NRMASK = (1 << _IOC_NRBITS) - 1;
        const int _IOC_TYPEMASK = (1 << _IOC_TYPEBITS) - 1;
        const int _IOC_SIZEMASK = (1 << _IOC_SIZEBITS) - 1;
        const int _IOC_DIRMASK = (1 << _IOC_DIRBITS) - 1;

        const int _IOC_NRSHIFT = 0;
        const int _IOC_TYPESHIFT = _IOC_NRSHIFT + _IOC_NRBITS;
        const int _IOC_SIZESHIFT = _IOC_TYPESHIFT + _IOC_TYPEBITS;
        const int _IOC_DIRSHIFT = _IOC_SIZESHIFT + _IOC_SIZEBITS;

        const int _IOC_NONE = 0;
        const int _IOC_WRITE = 1;
        const int _IOC_READ = 2;

        internal static int _IOC(int dir, int type, int nr, int size)
                => ((dir) << _IOC_DIRSHIFT) | ((type) << _IOC_TYPESHIFT) | ((nr) << _IOC_NRSHIFT) | ((size) << _IOC_SIZESHIFT);

        internal static int _IO(int type, int nr) => _IOC(_IOC_NONE, type, nr, 0);
        internal static int _IOR<T>(int type, int nr) => _IOC(_IOC_READ, type, nr, _IOC_TYPECHECK<T>());
        internal static int _IOW<T>(int type, int nr) => _IOC(_IOC_WRITE, type, nr, _IOC_TYPECHECK<T>());
        internal static int _IOWR<T>(int type, int nr) => _IOC(_IOC_READ | _IOC_WRITE, type, nr, _IOC_TYPECHECK<T>());
        internal static int _IOC_TYPECHECK<T>() => Marshal.SizeOf<T>();

        public static void xu_set_cur(int fd, byte selector, byte[] data)
        {
            unsafe
            {
                fixed (byte* dataptr = &data[0])
                {
                    var c = new uvc_xu_control_query()
                    {
                        unit = 4,
                        selector = selector,
                        query = _UVC_SET_CUR,
                        size = (ushort)data.Length,
                        data = (IntPtr)dataptr
                    };
                    ioctl(fd, (uint)_UVCIOC_CTRL_QUERY, (IntPtr)(&c));
                }
            }
        }

        public static byte[] xu_get_cur(int fd, byte selector, int len)
        {
            byte[] data = new byte[len];
            unsafe
            {
                fixed (byte* dataptr = &data[0])
                {
                    uvc_xu_control_query c = new uvc_xu_control_query()
                    {
                        unit = 4,
                        selector = selector,
                        query = _UVC_GET_CUR,
                        size = (ushort)data.Length,
                        data = (IntPtr)dataptr
                    };
                    if (ioctl(fd, (uint)_UVCIOC_CTRL_QUERY, (IntPtr)(&c)) != 0)
                        return data; //TODO: maybe throw here instead?
                }
            }
            return data;
        }

        public static void set_cur_no_resp(int fd, byte[] data)
        {
            xu_set_cur(fd, 2, data);
        }

        public static bool set_cur(int fd, byte[] data, int timeout = 1000)
        {
            xu_set_cur(fd, 2, data);
            CancellationTokenSource cts = new CancellationTokenSource(timeout);
            while (!cts.Token.IsCancellationRequested)
            {
                byte[] rcvdata = xu_get_cur(fd, 2, 384);
                switch (rcvdata[0])
                {
                    case 0x55:
                        break;
                    case 0x56:
                        return Enumerable.SequenceEqual(data[0..16], rcvdata[1..17]);
                    default:
                        return false;
                }
            }
            return false;
        }

        public static void set_register(int fd, int reg, int addr, int value)
        {
            byte[] data = new byte[384];

            data[0] = _XU_TASK_SET;
            data[1] = (byte)reg;
            data[2] = 0x60;
            data[3] = 1; // address len
            data[4] = 1; // value len
            // address
            data[5] = (byte)((addr >> 24) & 0xFF);
            data[6] = (byte)((addr >> 16) & 0xFF);
            data[7] = (byte)((addr >> 8) & 0xFF);
            data[8] = (byte)(addr & 0xFF);
            // page address
            data[9] = 0x90;
            data[10] = 0x01;
            data[11] = 0x00;
            data[12] = 0x01;
            // value
            data[13] = (byte)((value >> 24) & 0xFF);
            data[14] = (byte)((value >> 16) & 0xFF);
            data[15] = (byte)((value >> 8) & 0xFF);
            data[16] = (byte)(value & 0xFF);

            set_cur(fd, data);
        }

        public static int get_register(int fd, int reg, int addr)
        {
            byte[] data = new byte[384];

            data[0] = _XU_TASK_GET;
            data[1] = (byte)reg;
            data[2] = 0x60;
            data[3] = 1; // address len
            data[4] = 1; // value len
            // address
            data[5] = (byte)((addr >> 24) & 0xFF);
            data[6] = (byte)((addr >> 16) & 0xFF);
            data[7] = (byte)((addr >> 8) & 0xFF);
            data[8] = (byte)(addr & 0xFF);
            // page address
            data[9] = 0x90;
            data[10] = 0x01;
            data[11] = 0x00;
            data[12] = 0x01;
            // value
            data[13] = 0x00;
            data[14] = 0x00;
            data[15] = 0x00;
            data[16] = 0x00;
            data[254] = 0x53;
            data[255] = 0x54;

            set_cur(fd, data);
            return 0;
        }

        public static void set_register_sensor(int fd, int addr, int value)
        {
            set_register(fd, _XU_REG_SENSOR, addr, value);
        }

        public static void get_register_sensor(int fd, int addr)
        {
            get_register(fd, _XU_REG_SENSOR, addr);
        }

        public static void set_enable_stream(int fd, bool enable)
        {
            byte[] data = new byte[384];
            data[0] = _XU_TASK_SET;
            data[1] = 0x14;
            data[2] = 0x00;
            data[3] = (byte)(enable ? 0x01 : 0x00);
            data[254] = 0x53;
            data[255] = 0x54;
            set_cur(fd, data);
        }

        public static uint get_len(int fd)
        {
            uint length = 0;
            unsafe
            {
                uvc_xu_control_query c = new uvc_xu_control_query()
                {
                    unit = 4,
                    selector = 2,
                    query = _UVC_GET_LEN,
                    size = 2,
                    data = (IntPtr)(&length)
                };
                ioctl(fd, _UVCIOC_CTRL_QUERY, (IntPtr)(&c));
            }
            return length;
        }

        public static bool activate_tracker(int fd)
        {
            //uint l = get_len(fd);
            byte[] data = new byte[384];
            data[0] = 0x51;
            data[1] = 0x52;
            data[254] = 0x53;
            data[255] = 0x54;

            set_cur(fd, data);
            set_enable_stream(fd, false);
            set_cur(fd, data);

            // 0x02, 0x03 and 0x04 all control IR intensity.
            set_register_sensor(fd, 0x00, 0x40);
            set_register_sensor(fd, 0x08, 0x01);
            set_register_sensor(fd, 0x70, 0x00);
            set_register_sensor(fd, 0x02, 0xFF); //IR ON
            set_register_sensor(fd, 0x03, 0xFF); // IR ON
            set_register_sensor(fd, 0x04, 0xFF); // IR ON
            set_register_sensor(fd, 0x0e, 0x00);
            set_register_sensor(fd, 0x05, 0xb2);
            set_register_sensor(fd, 0x06, 0xb2);
            set_register_sensor(fd, 0x07, 0xb2);
            set_register_sensor(fd, 0x0f, 0x03);
            set_cur(fd, data);
            set_enable_stream(fd, true);
            return true;
        }

        public static bool deactivate_tracker(int fd)
        {
            //uint l = get_len(fd);
            byte[] data = new byte[384];
            data[0] = 0x51;
            data[1] = 0x52;
            data[254] = 0x53;
            data[255] = 0x54;

            set_cur(fd, data);
            set_enable_stream(fd, false);
            set_cur(fd, data);

            // 0x02, 0x03 and 0x04 all control IR intensity.
            set_register_sensor(fd, 0x00, 0x40);
            set_register_sensor(fd, 0x08, 0x01);
            set_register_sensor(fd, 0x70, 0x00);
            set_register_sensor(fd, 0x02, 0x00); //IR Off
            set_register_sensor(fd, 0x03, 0x00); // IR Off
            set_register_sensor(fd, 0x04, 0x00); // IR Off
            set_register_sensor(fd, 0x0e, 0x00);
            set_register_sensor(fd, 0x05, 0xb2);
            set_register_sensor(fd, 0x06, 0xb2);
            set_register_sensor(fd, 0x07, 0xb2);
            set_register_sensor(fd, 0x0f, 0x03);
            set_cur(fd, data);
            set_enable_stream(fd, true);
            return true;
        }
    }
}
