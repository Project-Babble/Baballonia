using System.Runtime.InteropServices;

namespace Baballonia.LibV4L2Capture.V4L2;

public static class Ioctl
{
    private const int IOC_NRBITS = 8;
    private const int IOC_TYPEBITS = 8;
    private const int IOC_SIZEBITS = 14;
    private const int IOC_DIRBITS = 2;

    private const int IOC_NRSHIFT = 0;
    private const int IOC_TYPESHIFT = IOC_NRSHIFT + IOC_NRBITS;
    private const int IOC_SIZESHIFT = IOC_TYPESHIFT + IOC_TYPEBITS;
    private const int IOC_DIRSHIFT = IOC_SIZESHIFT + IOC_SIZEBITS;

    private const int IOC_NONE = 0;
    private const int IOC_WRITE = 1;
    private const int IOC_READ = 2;

    private static uint IOC(int dir, int type, int nr, int size)
        => (uint)((dir << IOC_DIRSHIFT) | (type << IOC_TYPESHIFT) | (nr << IOC_NRSHIFT) | (size << IOC_SIZESHIFT));

    private static uint IO(char type, int nr) => IOC(IOC_NONE, type, nr, 0);
    private static uint IOR<T>(char type, int nr) => IOC(IOC_READ, type, nr, Marshal.SizeOf<T>());
    private static uint IOW<T>(char type, int nr) => IOC(IOC_WRITE, type, nr, Marshal.SizeOf<T>());
    private static uint IOWR<T>(char type, int nr) => IOC(IOC_READ | IOC_WRITE, type, nr, Marshal.SizeOf<T>());

    // V4L2 ioctl codes
    public static readonly uint VIDIOC_QUERYCAP = IOR<Data.v4l2_capability>('V', 0);
    public static readonly uint VIDIOC_RESERVED = IO('V', 1);
    public static readonly uint VIDIOC_ENUM_FMT = IOWR<Data.v4l2_fmtdesc>('V', 2);
    public static readonly uint VIDIOC_G_FMT = IOWR<Data.v4l2_format>('V', 4);
    public static readonly uint VIDIOC_S_FMT = IOWR<Data.v4l2_format>('V', 5);
    public static readonly uint VIDIOC_REQBUFS = IOWR<Data.v4l2_requestbuffers>('V', 8);
    public static readonly uint VIDIOC_QUERYBUF = IOWR<Data.v4l2_buffer>('V', 9);
    //public static readonly uint VIDIOC_G_FBUF = IOR<v4l2_framebuffer>('V', 10);
    //public static readonly uint VIDIOC_S_FBUF = IOW<v4l2_framebuffer>('V', 11);
    public static readonly uint VIDIOC_OVERLAY = IOW<int>('V', 14);
    public static readonly uint VIDIOC_QBUF = IOWR<Data.v4l2_buffer>('V', 15);
    //public static readonly uint VIDIOC_EXPBUF = IOWR<v4l2_exportbuffer>('V', 16);
    public static readonly uint VIDIOC_DQBUF = IOWR<Data.v4l2_buffer>('V', 17);
    public static readonly uint VIDIOC_STREAMON = IOW<int>('V', 18);
    public static readonly uint VIDIOC_STREAMOFF = IOW<int>('V', 19);
    //public static readonly uint VIDIOC_G_PARM = IOWR<v4l2_streamparm>('V', 21);
    //public static readonly uint VIDIOC_S_PARM = IOWR<v4l2_streamparm>('V', 22);
    public static readonly uint VIDIOC_G_STD = IOR<UInt64>('V', 23);
    public static readonly uint VIDIOC_S_STD = IOW<UInt64>('V', 24);
    //public static readonly uint VIDIOC_ENUMSTD = IOWR<v4l2_standard>('V', 25);
    //public static readonly uint VIDIOC_ENUMINPUT = IOWR<v4l2_input>('V', 26);
    //public static readonly uint VIDIOC_G_CTRL = IOWR<v4l2_control>('V', 27);
    //public static readonly uint VIDIOC_S_CTRL = IOWR<v4l2_control>('V', 28);
    //public static readonly uint VIDIOC_G_TUNER = IOWR<v4l2_tuner>('V', 29);
    //public static readonly uint VIDIOC_S_TUNER = IOW<v4l2_tuner>('V', 30);
    //public static readonly uint VIDIOC_G_AUDIO = IOR<v4l2_audio>('V', 33);
    //public static readonly uint VIDIOC_S_AUDIO = IOW<v4l2_audio>('V', 34);
    //public static readonly uint VIDIOC_QUERYCTRL = IOWR<v4l2_queryctrl>('V', 36);
    //public static readonly uint VIDIOC_QUERYMENU = IOWR<v4l2_querymenu>('V', 37);
    public static readonly uint VIDIOC_G_INPUT = IOR<int>('V', 38);
    public static readonly uint VIDIOC_S_INPUT = IOWR<int>('V', 39);
    //public static readonly uint VIDIOC_G_EDID = IOWR<v4l2_edid>('V', 40);
    //public static readonly uint VIDIOC_S_EDID = IOWR<v4l2_edid>('V', 41);
    public static readonly uint VIDIOC_G_OUTPUT = IOR<int>('V', 46);
    public static readonly uint VIDIOC_S_OUTPUT = IOWR<int>('V', 47);
    //public static readonly uint VIDIOC_ENUMOUTPUT = IOWR<v4l2_output>('V', 48);
    //public static readonly uint VIDIOC_G_AUDOUT = IOR<v4l2_audioout>('V', 49);
    //public static readonly uint VIDIOC_S_AUDOUT = IOW<v4l2_audioout>('V', 50);
    //public static readonly uint VIDIOC_G_MODULATOR = IOWR<v4l2_modulator>('V', 54);
    //public static readonly uint VIDIOC_S_MODULATOR = IOW<v4l2_modulator>('V', 55);
    //public static readonly uint VIDIOC_G_FREQUENCY = IOWR<v4l2_frequency>('V', 56);
    //public static readonly uint VIDIOC_S_FREQUENCY = IOW<v4l2_frequency>('V', 57);
    //public static readonly uint VIDIOC_CROPCAP = IOWR<v4l2_cropcap>('V', 58);
    //public static readonly uint VIDIOC_G_CROP = IOWR<v4l2_crop>('V', 59);
    //public static readonly uint VIDIOC_S_CROP = IOW<v4l2_crop>('V', 60);
    //public static readonly uint VIDIOC_G_JPEGCOMP = IOR<v4l2_jpegcompression>('V', 61);
    //public static readonly uint VIDIOC_S_JPEGCOMP = IOW<v4l2_jpegcompression>('V', 62);
    public static readonly uint VIDIOC_QUERYSTD = IOR<UInt64>('V', 63);
    public static readonly uint VIDIOC_TRY_FMT = IOWR<Data.v4l2_format>('V', 64);
    //public static readonly uint VIDIOC_ENUMAUDIO = IOWR<v4l2_audio>('V', 65);
    //public static readonly uint VIDIOC_ENUMAUDOUT = IOWR<v4l2_audioout>('V', 66);
    public static readonly uint VIDIOC_G_PRIORITY = IOR<uint>('V', 67);
    public static readonly uint VIDIOC_S_PRIORITY = IOW<uint>('V', 68);
    //public static readonly uint VIDIOC_G_SLICED_VBI_CAP = IOWR<v4l2_sliced_vbi_cap>('V', 69);
    public static readonly uint VIDIOC_LOG_STATUS = IO('V', 70);
    //public static readonly uint VIDIOC_G_EXT_CTRLS = IOWR<v4l2_ext_controls>('V', 71);
    //public static readonly uint VIDIOC_S_EXT_CTRLS = IOWR<v4l2_ext_controls>('V', 72);
    //public static readonly uint VIDIOC_TRY_EXT_CTRLS = IOWR<v4l2_ext_controls>('V', 73);
    public static readonly uint VIDIOC_ENUM_FRAMESIZES = IOWR<Data.v4l2_frmsizeenum>('V', 74);
    public static readonly uint VIDIOC_ENUM_FRAMEINTERVALS = IOWR<Data.v4l2_frmivalenum>('V', 75);
    //public static readonly uint VIDIOC_G_ENC_INDEX = IOR<v4l2_enc_idx>('V', 76);
    //public static readonly uint VIDIOC_ENCODER_CMD = IOWR<v4l2_encoder_cmd>('V', 77);
    //public static readonly uint VIDIOC_TRY_ENCODER_CMD = IOWR<v4l2_encoder_cmd>('V', 78);
}
