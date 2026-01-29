using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Baballonia.VFTCapture.Linux;

public partial class LinuxUsbCommunicator : IDisposable
{
    /// <summary>
    /// Native interop functions for Linux.
    /// </summary>
    private sealed partial class LinuxNative
    {
        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        public static partial int Close(IntPtr handle);
        [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        public static partial int Ioctl(IntPtr handle, int request, ref UvcXuControlQuery capability);

        [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
        public static partial IntPtr Open([MarshalAs(UnmanagedType.LPStr)] string path, FileOpenFlags flags);
    }

    public enum FileOpenFlags
    {
        O_RDONLY = 0x00,
        O_RDWR = 0x02,
        O_NONBLOCK = 0x800,
        O_SYNC = 0x101000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UvcXuControlQuery
    {
        public byte unit;
        public byte selector;
        public UvcQuery query;
        public ushort size;
        public IntPtr data;
    }

    public enum XuTask : byte
    {
        SET = 0x50,
        GET = 0x51,
    }

    public enum XuReg : byte
    {
        SENSOR = 0xab,
    }

    public enum UvcQuery : byte
    {
        SET_CUR = 0x01,
        GET_CUR = 0x81,
        GET_MIN = 0x82,
        GET_MAX = 0x83,
        GET_RES = 0x84,
        GET_LEN = 0x85,
        GET_INFO = 0x86,
        GET_DEF = 0x87,
    }

    const int _IOC_NRBITS = 8;
    const int _IOC_TYPEBITS = 8;
    const int _IOC_SIZEBITS = 14;
    const int _IOC_DIRBITS = 2;

    const int _IOC_NRSHIFT = 0;
    const int _IOC_TYPESHIFT = _IOC_NRSHIFT + _IOC_NRBITS;
    const int _IOC_SIZESHIFT = _IOC_TYPESHIFT + _IOC_TYPEBITS;
    const int _IOC_DIRSHIFT = _IOC_SIZESHIFT + _IOC_SIZEBITS;

    const int _IOC_NONE = 0;
    const int _IOC_WRITE = 1;
    const int _IOC_READ = 2;

    private static int Ioc(int dir, int type, int nr, int size)
        => (dir << _IOC_DIRSHIFT) | (type << _IOC_TYPESHIFT) | (nr << _IOC_NRSHIFT) | (size << _IOC_SIZESHIFT);
    private static int IocTypeCheck<T>() where T : unmanaged
        => Marshal.SizeOf<T>();
    private static readonly int _UVC_IOC_CTRL_QUERY = Ioc(_IOC_READ | _IOC_WRITE, 'u', 0x21, IocTypeCheck<UvcXuControlQuery>());

    /// <summary>
    /// The file handle. Should not really be used outside of this clase.
    /// </summary>
    internal IntPtr Handle { get; private set; } = IntPtr.Zero;

    /// <summary>
    /// Device control buffer size. For the Vive Face Tracker this
    /// is either 384 or 64.
    /// </summary>
    public ushort BufferSize { get; private set; } = 0;

    /// <summary>
    /// Check if the held handle is still valid.
    /// </summary>
    public bool IsValid { get => Handle != IntPtr.Zero; }

    private readonly ILogger log;

    /// <summary>
    /// Opens a file and wraps the handle. It must point to a Vive Face Tracker device.
    /// </summary>
    /// <exception cref="Exception"></exception>
    public LinuxUsbCommunicator(ILogger logger, string path, FileOpenFlags flags = FileOpenFlags.O_RDWR)
    {
        log = logger;

        log.LogDebug($"VFT: opening '{path}' as '{flags}'");
        Handle = LinuxNative.Open(path, flags);
        var err = Marshal.GetLastPInvokeError();
        if (err != 0)
            throw new Exception($"Error while opening native file:\n{Marshal.GetLastPInvokeErrorMessage()}");

        log.LogDebug("VFT: validating buffer size");
        var deviceBufferSz = GetLen();

        log.LogDebug($"VFT: get buffer size: {deviceBufferSz}");
        if (deviceBufferSz != 384 && deviceBufferSz != 64)
            throw new Exception($"Got unexpected device buffer size: {deviceBufferSz}");

        BufferSize = deviceBufferSz;
    }

    /// <summary>
    /// Explicit closing of the handle without waiting for GC.
    /// </summary>
    public void Dispose()
    {
        // We must ensure we actually hold a valid pointer.
        if (IsValid)
            LinuxNative.Close(Handle);
        // After closing, we must ensure the handle cannot be used anymore!
        Handle = IntPtr.Zero;
    }

    ~LinuxUsbCommunicator() => Dispose();

    private void XuQueryCur(byte unit, byte selector, UvcQuery query, byte[] data)
    {
        var dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            UvcXuControlQuery c = new()
            {
                unit = unit,
                selector = selector,
                query = query,
                size = (ushort)data.Length,
                data = dataHandle.AddrOfPinnedObject()
            };
            var res = LinuxNative.Ioctl(Handle, _UVC_IOC_CTRL_QUERY, ref c);
            if (res != 0)
            {
                int err = Marshal.GetLastPInvokeError();
                string msg = Marshal.GetLastPInvokeErrorMessage();
                throw new Exception(
                    $"Error in ioctl uvc_xu_control_query request:\n" +
                    $"  {{q:{query},sz:{data.Length}}}\n" +
                    $"  {{r:{res},e:{err}}}: {msg}");
            }
        }
        finally
        {
            dataHandle.Free();
        }
    }

    private void XuSetCur(byte selector, byte[] data)
        => XuQueryCur(4, selector, UvcQuery.SET_CUR, data);

    private byte[] XuGetCur(byte selector, int len)
    {
        byte[] data = new byte[len];
        XuQueryCur(4, selector, UvcQuery.GET_CUR, data);
        return data;
    }

    private ushort GetLen(byte selector = 2)
    {
        byte[] data = new byte[2];
        XuQueryCur(4, selector, UvcQuery.GET_LEN, data);
        return (ushort)(data[1] << 8 | data[0]);
    }

    private byte SetCur(byte[] data, int timeout = 1000)
    {
        if (data.Length != BufferSize)
            throw new Exception($"Got incorrect buffer size: {data.Length} expected: {BufferSize}");

        XuSetCur(2, data);

        if (timeout <= 0)
            return 0;

        CancellationTokenSource cts = new(timeout);
        while (!cts.Token.IsCancellationRequested)
        {
            byte[] result = XuGetCur(2, BufferSize);
            switch (result[0])
            {
                case 0x55: // Not ready.
                    // Can do a lil' eep.
                    Thread.Sleep(1);
                    break;
                case 0x56:
                    if (Enumerable.SequenceEqual(data[0..16], result[1..17]))
                    {
                        return result[17];
                    }
                    throw new Exception($"Invalid response sequence: {result[1..17]} expected: {data[0..16]}");
                default:
                    throw new Exception($"Unexpected response from XU command: {result[0]}");
            }
        }
        throw new TimeoutException("Got no rersponse from device.");
    }

    /// <summary>
    /// Applies a task to a register.
    /// </summary>
    private byte RegisterTask(XuTask task, XuReg reg, byte addr, byte value = 0x00)
    {
        byte[] data = new byte[BufferSize];

        // Because we only need to set bytes, we can hardcode
        // most of the values here.
        data[00] = (byte)task;
        data[01] = (byte)reg;
        data[02] = 0x60;
        data[03] = 0x01; // 1 byte sized address.
        data[04] = 0x01; // 1 byte sized value.
        // Address.
        data[05] = 0x00;
        data[06] = 0x00;
        data[07] = 0x00;
        data[08] = addr;
        // Page address.
        data[09] = 0x90;
        data[10] = 0x01;
        data[11] = 0x00;
        data[12] = 0x01;
        // Value.
        data[13] = 0x00;
        data[14] = 0x00;
        data[15] = 0x00;
        data[16] = value;

        return SetCur(data);
    }

    private void SetRegister(XuReg reg, byte addr, byte value)
        => RegisterTask(XuTask.SET, reg, addr, value);

    private byte GetRegister(XuReg reg, byte addr)
        => RegisterTask(XuTask.GET, reg, addr);

    private void SetRegisterSensor(byte addr, byte value)
        => SetRegister(XuReg.SENSOR, addr, value);

    private void GetRegisterSensor(byte addr)
        => GetRegister(XuReg.SENSOR, addr);

    private void SetEnableStream(bool enable)
    {
        log.LogDebug($"VFT: set stream: {(enable ? "on" : "off")}");
        byte[] data = new byte[BufferSize];
        data[0] = (byte)XuTask.SET;
        data[1] = 0x14; // Magic numbers, this does not have
        data[2] = 0x00; // the same pattern as RegisterTask!
        data[3] = (byte)(enable ? 0x01 : 0x00);
        SetCur(data);
    }

    private void SendMagicPacket()
    {
        // I have no clue why we need to send this magic packet.
        log.LogDebug("VFT: sending magic packet");
        byte[] data = new byte[BufferSize];
        data[0] = 0x51; // Magic numbers, this does not have
        data[1] = 0x52; // the same pattern as RegisterTask!
        if (BufferSize >= 256)
        {
            // Need to set magic numbers on large buffers.
            data[254] = 0x53;
            data[255] = 0x54;
        }
        SetCur(data);
    }

    /// <summary>
    /// Toggles the state of the camera.
    /// </summary>
    public void SetState(bool enabled)
    {
        SendMagicPacket();
        SetEnableStream(false);

        Thread.Sleep(100);

        // Set infra-red LED state.
        byte irLedState = (byte)(enabled ? 0xff : 0x00);
        log.LogDebug($"VFT: set camera: {(enabled ? "on" : "off")}");
        SendMagicPacket();
        SetRegisterSensor(0x00, 0x40);
        SetRegisterSensor(0x08, 0x01);
        SetRegisterSensor(0x70, 0x00);
        SetRegisterSensor(0x02, irLedState);
        SetRegisterSensor(0x03, irLedState);
        SetRegisterSensor(0x04, irLedState);
        SetRegisterSensor(0x0e, 0x00);
        SetRegisterSensor(0x05, 0xb2);
        SetRegisterSensor(0x06, 0xb2);
        SetRegisterSensor(0x07, 0xb2);
        SetRegisterSensor(0x0f, 0x03);

        // On enable, restore the stream.
        // On disable, skip this.
        if (enabled)
        {
            Thread.Sleep(100);

            SendMagicPacket();
            SetEnableStream(true);
        }
    }
}
