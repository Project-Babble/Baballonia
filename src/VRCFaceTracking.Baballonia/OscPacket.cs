using System.Buffers.Binary;
using System.Text;

namespace VRCFaceTracking.Baballonia;

/// <summary>
/// A minimal OSC 1.0 reader for BFR Link data: single-float messages, alone or in bundles (nested bundles too). Anything
/// else in a packet is skipped; a malformed packet stops at the first bad element. Also reads the two-boolean
/// initialization request (see <see cref="TryReadTwoBools"/>).
/// </summary>
internal static class OscPacket
{
    private const int MaxDepth = 4;

    /// <summary>Calls <paramref name="onFloat"/> for every float message in the packet.</summary>
    public static void ForEachFloat(ReadOnlySpan<byte> packet, Action<string, float> onFloat) =>
        Read(packet, onFloat, 0);

    private static void Read(ReadOnlySpan<byte> p, Action<string, float> onFloat, int depth)
    {
        if (p.Length >= 16 && p[..8].SequenceEqual("#bundle\0"u8))
        {
            if (depth >= MaxDepth)
                return;
            var at = 16;   // after the time tag
            while (at + 4 <= p.Length)
            {
                var size = BinaryPrimitives.ReadInt32BigEndian(p.Slice(at, 4));
                if (size <= 0 || at + 4 + size > p.Length)
                    return;
                Read(p.Slice(at + 4, size), onFloat, depth + 1);
                at += 4 + size;
            }
            return;
        }
        if (p.Length == 0 || p[0] != (byte)'/')
            return;
        var end = p.IndexOf((byte)0);
        if (end <= 0)
            return;
        var address = Encoding.ASCII.GetString(p[..end]);
        var tags = Padded(end + 1);
        // ",f" then padding: one float argument.
        if (tags + 8 > p.Length || p[tags] != (byte)',' || p[tags + 1] != (byte)'f' || p[tags + 2] != 0)
            return;
        var value = BinaryPrimitives.ReadSingleBigEndian(p.Slice(tags + 4, 4));
        onFloat(address, value);
    }

    /// <summary>Reads a single message with exactly two boolean arguments (type tags ",TT", ",TF", ",FT" or ",FF").</summary>
    public static bool TryReadTwoBools(ReadOnlySpan<byte> p, out string address, out bool first, out bool second)
    {
        address = "";
        first = second = false;
        if (p.Length == 0 || p[0] != (byte)'/')
            return false;
        var end = p.IndexOf((byte)0);
        if (end <= 0)
            return false;
        var tags = Padded(end + 1);
        // OSC booleans live in the type tags alone; they carry no argument bytes.
        if (tags + 4 > p.Length || p[tags] != (byte)',' || !IsBool(p[tags + 1]) || !IsBool(p[tags + 2]) || p[tags + 3] != 0)
            return false;
        address = Encoding.ASCII.GetString(p[..end]);
        first = p[tags + 1] == (byte)'T';
        second = p[tags + 2] == (byte)'T';
        return true;
    }

    private static bool IsBool(byte tag) => tag is (byte)'T' or (byte)'F';

    private static int Padded(int n) => (n + 3) & ~3;
}
