using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaMiaDev.OSC;

public class OscMessage
{
    public OscMessageMeta Meta;
    private IntPtr _metaPtr;

    public string Address
    {
        get => Meta.Address;
        set => Meta.Address = value;
    }

    private readonly Action<object> _valueSetter;

    public object? Value
    {
        get
        {
            if (Meta.ValueLength == 0)
            {
                return null!;
            }

            var values = new OscValue[Meta.ValueLength];
            var ptr = Meta.Value;
            for (var i = 0; i < Meta.ValueLength; i++)
            {
                values[i] = Marshal.PtrToStructure<OscValue>(ptr);
                ptr += Marshal.SizeOf<OscValue>();
            }

            return values[0].Value;
        }
        set => _valueSetter(value!);
    }

    public OscMessage(string address, Type type)
    {
        Address = address;
        var oscType = OscUtils.TypeConversions.FirstOrDefault(conv => conv.Key.Item1 == type).Value;

        if (oscType != default)
        {
            Meta.ValueLength = 1;
            Meta.Value = Marshal.AllocHGlobal(Marshal.SizeOf<OscValue>() * Meta.ValueLength);
            var oscValue = new OscValue
            {
                Type = oscType.oscType,
            };
            _valueSetter = value =>
            {
                oscValue.Value = value;
                Marshal.StructureToPtr(oscValue, Meta.Value, false);
            };
        }
        else    // If we don't have the type, we assume it's a struct and serialize it using reflection
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Meta.ValueLength = fields.Length;
            Meta.Value = Marshal.AllocHGlobal(Marshal.SizeOf<OscValue>() * Meta.ValueLength);
            var values = new OscValue[Meta.ValueLength];
            for (var i = 0; i < Meta.ValueLength; i++)
            {
                values[i] = new OscValue
                {
                    Type = OscUtils.TypeConversions.First(conv => conv.Key.Item1 == fields[i].FieldType).Value.oscType,
                };
            }
            _valueSetter = value =>
            {
                for (var j = 0; j < Meta.ValueLength; j++)
                {
                    values[j].Value = fields[j].GetValue(value);
                    Marshal.StructureToPtr(values[j], Meta.Value + Marshal.SizeOf<OscValue>() * j, false);
                }
            };
        }
    }

    public static OscMessage TryParseOsc(byte[] bytes, int len, ref int messageIndex)
    {
        var msg = new OscMessage(bytes, len, ref messageIndex);
        if (msg._metaPtr == IntPtr.Zero)
        {
            return null!;
        }

        return msg;
    }

    public OscMessage(byte[] bytes, int len, ref int messageIndex)
    {
        _metaPtr = FtiOsc.parse_osc(bytes, len, ref messageIndex);
        if (_metaPtr != IntPtr.Zero)
        {
            Meta = Marshal.PtrToStructure<OscMessageMeta>(_metaPtr);
        }
    }

    /// <summary>
    /// Encodes stored osc meta into raw bytes using fti_osc lib
    /// </summary>
    /// <param name="buffer">Target byte buffer to serialize to, starting from index 0</param>
    /// <param name="ct">Cancellation Token</param>
    /// <returns>Length of serialized data</returns>
    public async Task<int> Encode(byte[] buffer, CancellationToken ct) => await Task.Run(() => FtiOsc.create_osc_message(buffer, ref Meta), ct);

    public OscMessage(OscMessageMeta meta) => Meta = meta;

    ~OscMessage()
    {
        // If we don't own this memory, then we need to sent it back to rust to free it
        if (_metaPtr != IntPtr.Zero)
        {
            FtiOsc.free_osc_message(_metaPtr);
        }
    }
}
