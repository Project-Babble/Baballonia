using System.Runtime.InteropServices;
using System.Text;

namespace Baballonia.VFTCapture.Linux.V4L2;

public class Data {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct v4l2_capability
    {
        public fixed byte driver[16];
        public fixed byte card[32];
        public fixed byte bus_info[32];

        public uint version;
        public V4L2Capabilities capabilities;
        public uint device_caps;
        public fixed uint reserved[3];

        public bool HasFlag(V4L2Capabilities flag)
        {
            return (capabilities & flag) != 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct v4l2_fmtdesc
    {
        public uint index;
        public v4l2_buf_type type;
        public uint flags;

        public fixed byte description[32]; // fixed buffer instead of byte[]
        public v4l2_pix_fmt pixelformat;
        public fixed uint reserved[4];

        public string StringDescription
        {
            get
            {
                fixed (byte* p = description)
                {
                    int len = 0;
                    while (len < 32 && p[len] != 0) len++;
                    return Encoding.ASCII.GetString(p, len);
                }
            }
        }
    }

    public const int VIDEO_MAX_PLANES = 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct v4l2_pix_format
    {
        public uint width;
        public uint height;
        public v4l2_pix_fmt pixelformat;
        public uint field;
        public uint bytesperline;
        public uint sizeimage;
        public uint colorspace;
        public uint priv;
        public uint flags;
        public uint ycbcr_enc;
        public uint quantization;
        public uint xfer_func;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct v4l2_rect
    {
        public uint   left;
        public uint   top;
        public uint   width;
        public uint   height;
    };

    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 208)] // I have no idea why this isn't 204, but check in C the size of the struct is 208
    public unsafe struct v4l2_format
    {
        [FieldOffset(0)] public v4l2_buf_type type;

        [FieldOffset(8)] public v4l2_pix_format pix;

        // Replace byte[] with fixed buffer
        [FieldOffset(8)]
        public fixed byte raw_data[200];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct v4l2_fract
    {
        public uint numerator;
        public uint denominator;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct v4l2_frmival_stepwise
    {
        public v4l2_fract min;
        public v4l2_fract max;
        public v4l2_fract step;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct v4l2_frmsize_discrete
    {
        public uint width;
        public uint height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct v4l2_frmsize_stepwise
    {
        public uint min_width;
        public uint max_width;
        public uint step_width;
        public uint min_height;
        public uint max_height;
        public uint step_height;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public unsafe struct v4l2_frmsizeenum
    {
        [FieldOffset(0)] public uint index;
        [FieldOffset(4)] public v4l2_pix_fmt pixel_format;
        [FieldOffset(8)] public uint type;

        // Union: discrete or stepwise
        [FieldOffset(12)] public v4l2_frmsize_discrete discrete;
        [FieldOffset(12)] public v4l2_frmsize_stepwise stepwise;

        [FieldOffset(36)]
        public fixed uint reserved[2];
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public unsafe struct v4l2_frmivalenum
    {
        [FieldOffset(0)] public uint index;
        [FieldOffset(4)] public v4l2_pix_fmt pixel_format;
        [FieldOffset(8)] public uint width;
        [FieldOffset(12)] public uint height;
        [FieldOffset(16)] public v4l2_frmivaltypes type;

        // Union: discrete or stepwise
        [FieldOffset(20)] public v4l2_fract discrete;
        [FieldOffset(20)] public v4l2_frmival_stepwise stepwise;

        [FieldOffset(44)]
        public fixed uint reserved[2];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct v4l2_requestbuffers
    {
        public uint count;
        public v4l2_buf_type type;
        public v4l2_memory memory;

        public fixed uint reserved[2];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct timeval
    {
        public long tv_sec;   // seconds
        public long tv_usec;  // microseconds
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct v4l2_timecode
    {
        public uint type;
        public uint flags;
        public byte frames;
        public byte seconds;
        public byte minutes;
        public byte hours;
        public fixed byte userbits[4];
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 88)] // same as for v4l2_format??
    public struct v4l2_buffer
    {
        [FieldOffset(0)] public uint index;
        [FieldOffset(4)] public v4l2_buf_type type;
        [FieldOffset(8)] public uint bytesused;
        [FieldOffset(12)] public uint flags;
        [FieldOffset(16)] public uint field;
        [FieldOffset(24)] public timeval timestamp;
        [FieldOffset(40)] public v4l2_timecode timecode;
        [FieldOffset(56)] public uint sequence;

        [FieldOffset(60)] public v4l2_memory memory;

        // Union of memory offsets/pointers
        [FieldOffset(64)] public uint offset;       // for MMAP
        [FieldOffset(64)] public UIntPtr userptr;  // for USERPTR
        //[FieldOffset(64)] public v4l2_plane* planes;    // pointer to v4l2_plane array
        [FieldOffset(64)] public int fd;           // for DMABUF

        [FieldOffset(72)] public uint length;
        [FieldOffset(76)] public uint reserved2;
        [FieldOffset(80)] public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct pollfd
    {
        public int fd;
        public short events;
        public short revents;
    }

    public const short POLLIN  = 0x0001;
    public const short POLLERR = 0x0008;
    public const short POLLHUP = 0x0010;
}
