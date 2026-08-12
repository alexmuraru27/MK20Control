using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Mk20Control.Protocol.Codecs;

/// <summary>
/// Codec for the simple, untagged string/string map format used by FIND_DEVICE and
/// GET_DEVICE_THEME replies - CONFIRMED against real hardware (not the doc, not a
/// capture-only decode: this was verified by connecting to a physical MK20 over its serial
/// port and dumping the raw reply bytes).
///
/// This is a DIFFERENT wire format from <see cref="VariantMapCodec"/>'s typeId-tagged
/// values. Every value here - even things that look numeric, like a CRC32 or a volume
/// level - is stored as a plain length-prefixed UTF-16BE string, with no type tag and no
/// isNull byte:
///
///   map:   count(u32 BE) + count * (string key + string value)
///   string: byteLen(u32 BE) + UTF-16BE bytes (no null sentinel observed for this format)
///
/// Confirmed decoded FIND_DEVICE reply (8 entries): "version"->"V2.32",
/// "upgradeToLatestMethod"->"1", "screen_width"->"640", "screen_model"->"MK20",
/// "screen_height"->"656", "deviceVolume"->"7", "deviceName"->"ScreenKey",
/// "deviceBl"->"80".
///
/// Confirmed decoded GET_DEVICE_THEME reply: "bytesTotal"->"2648",
/// "bytesAvailable"->"2483", then one entry per installed theme where the KEY is the
/// device-side .Theme path and the VALUE is its CRC32 rendered as decimal text (e.g.
/// "/data/theme/MK20/字母/字母.Theme" -> "2626723596").
///
/// Earlier project notes had assumed these two commands used the same typeId-tagged
/// format as DEVICE_ProactiveEscalationCMD/.Theme files; that assumption was WRONG and
/// was only caught by testing structured decoding against real hardware rather than the
/// looser best-effort string-scanning fallback used for capture display purposes.
/// </summary>
public static class SimpleStringMapCodec
{
    /// <summary>Decodes a simple string/string map payload.</summary>
    /// <exception cref="InvalidDataException">Thrown for truncated or implausible data.</exception>
    public static List<KeyValuePair<string, string>> Decode(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var result = new List<KeyValuePair<string, string>>();
        int pos = 0;
        if (payload.Length < 4) return result;
        uint count = ReadUInt32(payload, ref pos);
        if (count > 10_000) throw new InvalidDataException($"Implausible simple string-map entry count {count}.");
        for (int i = 0; i < count; i++)
        {
            string key = ReadString(payload, ref pos);
            string value = ReadString(payload, ref pos);
            result.Add(new KeyValuePair<string, string>(key, value));
        }
        return result;
    }

    /// <summary>
    /// Attempts to decode a payload as a simple string/string map, requiring the parse to
    /// consume the entire payload with no leftover bytes (a stricter guard than
    /// <see cref="Decode"/>, suitable for auto-detecting this format among other candidates
    /// - e.g. distinguishing it from <see cref="VariantMapCodec"/>'s payloads).
    /// </summary>
    public static bool TryDecode(byte[] payload, out List<KeyValuePair<string, string>> result)
    {
        ArgumentNullException.ThrowIfNull(payload);
        result = new List<KeyValuePair<string, string>>();
        try
        {
            int pos = 0;
            if (payload.Length < 4) return false;
            uint count = ReadUInt32(payload, ref pos);
            if (count == 0 || count > 10_000) return false;
            for (int i = 0; i < count; i++)
            {
                string key = ReadString(payload, ref pos);
                string value = ReadString(payload, ref pos);
                result.Add(new KeyValuePair<string, string>(key, value));
            }
            if (pos != payload.Length) { result.Clear(); return false; }
            return true;
        }
        catch (InvalidDataException)
        {
            result.Clear();
            return false;
        }
    }

    /// <summary>Encodes a simple string/string map payload (byte-exact inverse of <see cref="Decode"/>).</summary>
    public static byte[] Encode(IReadOnlyCollection<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        using var stream = new MemoryStream();
        WriteUInt32(stream, (uint)values.Count);
        foreach (var (key, value) in values)
        {
            WriteString(stream, key);
            WriteString(stream, value);
        }
        return stream.ToArray();
    }

    private static string ReadString(byte[] data, ref int pos)
    {
        uint byteLen = ReadUInt32(data, ref pos);
        const uint maxPlausibleLength = 1024 * 1024;
        if (byteLen > maxPlausibleLength) throw new InvalidDataException($"Implausible string length {byteLen} at position {pos}.");
        if (pos + byteLen > data.Length)
            throw new InvalidDataException($"Truncated data: expected {byteLen} more byte(s) for a string at position {pos}, but only {data.Length - pos} remain.");
        var chars = new char[byteLen / 2];
        for (int i = 0; i < chars.Length; i++)
        {
            int b = pos + i * 2;
            chars[i] = (char)((data[b] << 8) | data[b + 1]);
        }
        pos += (int)byteLen;
        return new string(chars);
    }

    private static void WriteString(Stream stream, string value)
    {
        WriteUInt32(stream, (uint)(value.Length * 2));
        foreach (char c in value)
        {
            stream.WriteByte((byte)(c >> 8));
            stream.WriteByte((byte)(c & 0xFF));
        }
    }

    private static uint ReadUInt32(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length)
            throw new InvalidDataException($"Truncated data: expected 4 more byte(s) for a u32 field at position {pos}, but only {data.Length - pos} remain.");
        uint v = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
        pos += 4;
        return v;
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
