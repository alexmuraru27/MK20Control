using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Mk20Control.Protocol.Codecs;

/// <summary>
/// Encodes/decodes the SEND_SYSTEM_DATA_TO_DEVICE payload: a length-prefixed sequence of
/// string key/value pairs. The outer count and each string's byte-length prefix are
/// BIG-ENDIAN (unlike the frame header's u32 fields, which are little-endian - two
/// different serializers are in play: the outer envelope and the inner payload encoding).
/// Confirmed on real hardware pushing values like "GPU Usage" -> "0%", "CPU Usage" -> "21%".
/// </summary>
public static class SystemDataCodec
{
    /// <summary>Decodes a SEND_SYSTEM_DATA_TO_DEVICE payload into an ordered key/value sequence.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Decode(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var result = new List<KeyValuePair<string, string>>();
        int pos = 0;
        if (payload.Length < 4) return result;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(0, 4));
        pos = 4;
        for (int i = 0; i < count && pos + 4 <= payload.Length; i++)
        {
            string? key = ReadLengthPrefixedUtf16(payload, ref pos);
            string? value = ReadLengthPrefixedUtf16(payload, ref pos);
            if (key is null) break;
            result.Add(new KeyValuePair<string, string>(key, value ?? ""));
        }
        return result;
    }

    /// <summary>
    /// Encodes a key/value sequence into the SEND_SYSTEM_DATA_TO_DEVICE wire payload format
    /// (the exact inverse of <see cref="Decode"/>). This has been validated by round-tripping
    /// (encode then decode and comparing) but has NOT been confirmed to be accepted by real
    /// hardware when sent host-to-device with a caller-supplied key/value set beyond the
    /// data-source names actually observed on the wire (e.g. "GPU Usage", "CPU Usage",
    /// "device_bl", "Volume") - the device's theme must declare a matching
    /// `system_data_name` binding for a pushed key to have any visible effect.
    /// </summary>
    public static byte[] Encode(IReadOnlyCollection<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        using var stream = new System.IO.MemoryStream();
        Span<byte> countBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(countBuffer, (uint)values.Count);
        stream.Write(countBuffer);

        foreach (var kv in values)
        {
            WriteLengthPrefixedUtf16(stream, kv.Key);
            WriteLengthPrefixedUtf16(stream, kv.Value);
        }
        return stream.ToArray();
    }

    private static string? ReadLengthPrefixedUtf16(byte[] payload, ref int pos)
    {
        if (pos + 4 > payload.Length) return null;
        uint byteLen = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(pos, 4));
        pos += 4;
        if (byteLen == 0) return "";
        if (pos + byteLen > payload.Length) return null;
        // Observed as big-endian UTF-16 code units.
        var chars = new char[byteLen / 2];
        for (int i = 0; i < chars.Length; i++)
        {
            int b = pos + i * 2;
            chars[i] = (char)((payload[b] << 8) | payload[b + 1]);
        }
        pos += (int)byteLen;
        return new string(chars);
    }

    private static void WriteLengthPrefixedUtf16(System.IO.MemoryStream stream, string value)
    {
        Span<byte> lengthBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBuffer, (uint)(value.Length * 2));
        stream.Write(lengthBuffer);
        foreach (char c in value)
        {
            stream.WriteByte((byte)(c >> 8));
            stream.WriteByte((byte)(c & 0xFF));
        }
    }
}
