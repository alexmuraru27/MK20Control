using System.Buffers.Binary;

namespace Mk20Control.Protocol.Compat;

/// <summary>
/// Binary helpers missing from .NET Framework: big-endian <see cref="double"/>
/// conversion and hex formatting.
/// </summary>
internal static class BinaryCompat
{
    private const string HexDigits = "0123456789ABCDEF";

    public static double ReadDoubleBigEndian(ReadOnlySpan<byte> source) =>
        BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(source));

    public static void WriteDoubleBigEndian(Span<byte> destination, double value) =>
        BinaryPrimitives.WriteInt64BigEndian(destination, BitConverter.DoubleToInt64Bits(value));

    /// <summary>Uppercase hex, matching <c>Convert.ToHexString</c>.</summary>
    public static string ToHexString(ReadOnlySpan<byte> bytes)
    {
        char[] chars = new char[bytes.Length * 2];

        for (int index = 0; index < bytes.Length; index++)
        {
            chars[index * 2] = HexDigits[bytes[index] >> 4];
            chars[(index * 2) + 1] = HexDigits[bytes[index] & 0xF];
        }

        return new string(chars);
    }
}
