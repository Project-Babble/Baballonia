using System.Runtime.InteropServices;

namespace Baballonia.LibV4L2Capture.V4L2;


public class Device : IDisposable {
    public void Dispose()
    {
        StopCapture();

        for (int i = 0; i < _bufferStarts.Length; i++)
        {
            if (_bufferStarts[i] != IntPtr.Zero && _bufferLengths[i] > 0)
                NativeMethods.munmap(_bufferStarts[i], _bufferLengths[i]);
        }

        Data.v4l2_requestbuffers req = default;
        req.count = 0;
        req.type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;
        req.memory = v4l2_memory.V4L2_MEMORY_MMAP;
        NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_REQBUFS, ref req);

        _bufferCount = 0;

        if (_fileDescriptor >= 0)
        {
            NativeMethods.v4l2_close(_fileDescriptor);
            _fileDescriptor = -1;
        }


    }

    private const int O_RDWR = 2;

    public string Address { get; private set; }
    public bool Connected { get; private set; }

    private int _fileDescriptor;

    private IntPtr[] _bufferStarts;
    private uint[] _bufferLengths;
    private uint _bufferCount;

    public static Device? Connect(string address)
    {
        Device device = new Device
        {
            Address = address
        };

        if (!device.AttemptOpen())
            return null;
        Data.v4l2_capability caps = device.GetCapabilities();

        if (!caps.HasFlag(V4L2Capabilities.VIDEO_CAPTURE))
            throw new Exception("Device cannot capture video");

        if (!caps.HasFlag(V4L2Capabilities.STREAMING))
            throw new Exception("Device does not support streaming (required for mmap or userptr buffers)");

        var formats = device.GetFormats().Where(f => f.pixelformat == v4l2_pix_fmt.V4L2_PIX_FMT_MJPEG).ToList();

        if (formats.Count <= 0)
            throw new Exception("Device does not support MJPEG");

        var format = formats[0];

        Data.v4l2_frmivalenum bestInterval = default;
        double maxFps = 0;
        uint maxResolution = 0;

        var sizes = device.EnumerateFrameSizes(format.pixelformat);
        foreach (var size in sizes)
        {
            var intervals = device.EnumerateFrameIntervals(format.pixelformat, size.discrete.width, size.discrete.height);
            foreach (var interval in intervals)
            {
                double fps = 0d;
                switch (interval.type)
                {
                    case v4l2_frmivaltypes.V4L2_FRMIVAL_TYPE_DISCRETE:
                        fps = (double)interval.discrete.denominator / interval.discrete.numerator;
                        break;
                    case v4l2_frmivaltypes.V4L2_FRMIVAL_TYPE_CONTINUOUS:
                    case v4l2_frmivaltypes.V4L2_FRMIVAL_TYPE_STEPWISE:
                        fps = (double)interval.stepwise.min.denominator / interval.stepwise.min.numerator;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                uint resolution = size.discrete.width * size.discrete.height;

                if (fps > maxFps || (Math.Abs(fps - maxFps) < 0.001 && resolution > maxResolution))
                {
                    maxFps = fps;
                    maxResolution = resolution;
                    bestInterval = interval;
                }
            }
        }

        device.SetFormat(bestInterval);

        return device;
    }

    public Data.v4l2_capability GetCapabilities()
    {
        Data.v4l2_capability cap = default;
        int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_QUERYCAP, ref cap);

        if (ret < 0)
            throw new Exception($"VIDIOC_QUERYCAP failed: errno={Marshal.GetLastWin32Error()}");
        return cap;
    }

    public List<Data.v4l2_fmtdesc> GetFormats()
    {
        List<Data.v4l2_fmtdesc> formats = new List<Data.v4l2_fmtdesc>();
        uint index = 0;
        v4l2_buf_type type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;

        while (true)
        {
            // declare fmt inside unsafe
            Data.v4l2_fmtdesc fmt = default;
            fmt.index = index;
            fmt.type = type;
            fmt.flags = 0;
            fmt.pixelformat = 0;

            int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_ENUM_FMT, ref fmt);
            if (ret < 0)
                break; // no more formats

            formats.Add(fmt);
            index++;
        }

        return formats;
    }

    private Data.v4l2_format GetCurrentFormat()
    {
        Data.v4l2_format fmt = default;
        fmt.type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;

        int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_G_FMT, ref fmt);
        if (ret < 0)
            throw new Exception($"VIDIOC_G_FMT failed: errno={Marshal.GetLastWin32Error()}");

        return fmt;
    }

    public void SetFormat(Data.v4l2_frmivalenum format)
    {
        Data.v4l2_format fmt = default;
        fmt.type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;
        fmt.pix.width = format.width;
        fmt.pix.height = format.height;
        fmt.pix.pixelformat = format.pixel_format;

        int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_S_FMT, ref fmt);
        if (ret < 0)
            throw new Exception($"VIDIOC_S_FMT failed: errno={Marshal.GetLastWin32Error()}");
    }

    public List<Data.v4l2_frmsizeenum> EnumerateFrameSizes(v4l2_pix_fmt pixelformat)
    {
        List<Data.v4l2_frmsizeenum> sizes = new List<Data.v4l2_frmsizeenum>();
        uint index = 0;

        while (true)
        {
            Data.v4l2_frmsizeenum fsize = default;
            fsize.index = index;
            fsize.pixel_format = pixelformat;

            int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_ENUM_FRAMESIZES, ref fsize);

            if (ret < 0) break;
            sizes.Add(fsize);
            index++;
        }

        return sizes;
    }

    public List<Data.v4l2_frmivalenum> EnumerateFrameIntervals(v4l2_pix_fmt pixelformat, uint width, uint height)
    {
        List<Data.v4l2_frmivalenum> intervals = new List<Data.v4l2_frmivalenum>();
        uint index = 0;

        while (true)
        {
            Data.v4l2_frmivalenum fival = default;
            fival.index = index;
            fival.pixel_format = pixelformat;
            fival.width = width;
            fival.height = height;

            int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_ENUM_FRAMEINTERVALS, ref fival);

            if (ret < 0) break;
            intervals.Add(fival);
            index++;
        }

        return intervals;
    }

    private Data.v4l2_requestbuffers GetBuffers()
    {
        Data.v4l2_requestbuffers req = default;
        req.count = 3;
        req.type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;
        req.memory = v4l2_memory.V4L2_MEMORY_MMAP;

        int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_REQBUFS, ref req);
        if (ret < 0) throw new Exception($"VIDIOC_REQBUFS failed: errno={Marshal.GetLastWin32Error()}");

        return req;
    }

    public void InitMMapBuffers()
    {
        var req = GetBuffers();
        _bufferCount = req.count;

        _bufferStarts = new IntPtr[_bufferCount];
        _bufferLengths = new uint[_bufferCount];

        for (uint i = 0; i < _bufferCount; i++)
        {
            Data.v4l2_buffer buf = default;
            buf.type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;
            buf.memory = v4l2_memory.V4L2_MEMORY_MMAP;
            buf.index = i;

            int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_QUERYBUF, ref buf);
            if (ret < 0) throw new Exception($"VIDIOC_QUERYBUF failed: errno={Marshal.GetLastWin32Error()}");

            _bufferLengths[i] = buf.length;
            _bufferStarts[i] = NativeMethods.mmap(
                IntPtr.Zero, buf.length,
                Prot.PROT_READ | Prot.PROT_WRITE,
                MapFlags.MAP_SHARED,
                _fileDescriptor, new IntPtr(buf.offset));

            if (_bufferStarts[i] == (IntPtr)(-1))
                throw new Exception($"mmap failed: errno={Marshal.GetLastWin32Error()}");
        }
    }

    public void QueueAllBuffers()
    {
        for (uint i = 0; i < _bufferCount; i++)
        {
            Data.v4l2_buffer buf = default;
            buf.type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;
            buf.memory = v4l2_memory.V4L2_MEMORY_MMAP;
            buf.index = i;

            int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_QBUF, ref buf);
            if (ret < 0) throw new Exception($"VIDIOC_QBUF failed: errno={Marshal.GetLastWin32Error()}");
        }
    }

    public void StartStreaming()
    {
        v4l2_buf_type type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;
        int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_STREAMON, ref type);
        if (ret < 0) throw new Exception($"VIDIOC_STREAMON failed: errno={Marshal.GetLastWin32Error()}");
    }

    public byte[] CaptureFrame()
    {
        Data.v4l2_buffer buf = default;
        buf.type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;
        buf.memory = v4l2_memory.V4L2_MEMORY_MMAP;

        int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_DQBUF, ref buf);
        if (ret < 0) throw new Exception($"VIDIOC_DQBUF failed: errno={Marshal.GetLastWin32Error()}");

        byte[] frame = new byte[buf.bytesused];
        Marshal.Copy(_bufferStarts[buf.index], frame, 0, (int)buf.bytesused);

        ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_QBUF, ref buf);
        if (ret < 0) throw new Exception($"VIDIOC_QBUF failed: errno={Marshal.GetLastWin32Error()}");

        return frame;
    }

    public void StopStreaming()
    {
        v4l2_buf_type type = v4l2_buf_type.V4L2_BUF_TYPE_VIDEO_CAPTURE;
        int ret = NativeMethods.v4l2_ioctl_safe(_fileDescriptor, Ioctl.VIDIOC_STREAMOFF, ref type);
        if (ret < 0) throw new Exception($"VIDIOC_STREAMOFF failed: errno={Marshal.GetLastWin32Error()}");
    }

    public void StartCapture()
    {
        var f = GetCurrentFormat();
        InitMMapBuffers();
        QueueAllBuffers();
        StartStreaming();
    }

    public void StopCapture()
    {
        StopStreaming();
    }

    private bool AttemptOpen()
    {
        _fileDescriptor = NativeMethods.v4l2_open(Address, O_RDWR);
        return _fileDescriptor >= 0;
    }
}
