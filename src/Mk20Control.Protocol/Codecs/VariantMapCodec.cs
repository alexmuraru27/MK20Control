using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mk20Control.Protocol.Codecs;

/// <summary>
/// A single primitive value decoded from the tagged-value serialization format used
/// throughout the MK20 protocol (both in DEVICE_ProactiveEscalationCMD wire events and in
/// on-disk .Theme files). Exactly one of the properties is meaningful, matching
/// <see cref="TypeId"/>:
///
///   TypeId 1  -> <see cref="AsBool"/>
///   TypeId 2  -> <see cref="AsInt32"/>
///   TypeId 6  -> <see cref="AsDouble"/>
///   TypeId 8  -> <see cref="AsMap"/>
///   TypeId 9  -> <see cref="AsList"/>
///   TypeId 10 -> <see cref="AsString"/>
///   TypeId 12 -> <see cref="AsByteArray"/>
///
/// This is exposed for advanced/diagnostic use; most callers should prefer the strongly
/// typed <see cref="Mk20Control.Protocol.Theme"/> model produced by <c>ThemeFileCodec</c>.
/// </summary>
public readonly struct TaggedValue
{
    public required uint TypeId { get; init; }
    public bool IsNull { get; init; }
    public bool? AsBool { get; init; }
    public int? AsInt32 { get; init; }
    public double? AsDouble { get; init; }
    public string? AsString { get; init; }
    public byte[]? AsByteArray { get; init; }
    public Dictionary<string, TaggedValue>? AsMap { get; init; }
    public List<TaggedValue>? AsList { get; init; }

    public static TaggedValue Null(uint typeId) => new() { TypeId = typeId, IsNull = true };
    public static TaggedValue Of(bool value) => new() { TypeId = 1, AsBool = value };
    public static TaggedValue Of(int value) => new() { TypeId = 2, AsInt32 = value };
    public static TaggedValue Of(double value) => new() { TypeId = 6, AsDouble = value };
    public static TaggedValue Of(string value) => new() { TypeId = 10, AsString = value };
    public static TaggedValue Of(byte[] value) => new() { TypeId = 12, AsByteArray = value };
    public static TaggedValue Of(Dictionary<string, TaggedValue> value) => new() { TypeId = 8, AsMap = value };
    public static TaggedValue Of(List<TaggedValue> value) => new() { TypeId = 9, AsList = value };
}

/// <summary>
/// Low-level codec for the tagged-value serialization format reverse-engineered
/// byte-by-byte from a live capture, used both by DEVICE_ProactiveEscalationCMD wire event
/// payloads and by on-disk .Theme files. Confirmed layout:
///
///   map:          count(u32 BE) + count * (string key + tagged value)
///   tagged value: typeId(u32 BE) + isNull(u8) + type-specific data
///     typeId 1  = bool (1 byte)
///     typeId 2  = Int32 (4 bytes BE) - also used for what look like bool 0/1 flags in
///                 some observed frames
///     typeId 6  = Double (8 bytes BE)
///     typeId 8  = nested map (count-prefixed, as above)
///     typeId 9  = list (count(u32 BE) + count * tagged value)
///     typeId 10 = string (byteLen(u32 BE) + UTF-16BE bytes; byteLen=0xFFFFFFFF => null)
///     typeId 12 = byte array (byteLen(u32 BE) + raw bytes; byteLen=0xFFFFFFFF => null)
///
/// Encode methods are the byte-exact inverse of the corresponding Decode methods and have
/// been validated by round-tripping (encode then decode and compare) - see
/// <c>Mk20Control.Protocol.Tests</c> if present, or <c>CaptureAnalyzer --selftest</c>.
/// Sending a *device-accepted* re-encoded payload has only been confirmed for
/// <c>ThemeFileCodec</c>-produced .Theme files that were round-tripped through decode; other
/// uses of Encode should be verified against real hardware before being relied upon.
///
/// Unknown/unobserved typeIds are surfaced as a thrown <see cref="InvalidDataException"/>
/// rather than silently guessed at or dropped, per this library's "never assume" policy.
/// </summary>
public static class VariantMapCodec
{
    private const uint NullStringOrByteArrayLength = 0xFFFFFFFF;

    /// <summary>
    /// Attempts to decode a payload that is an outer array of maps (the shape observed for
    /// DEVICE_ProactiveEscalationCMD payloads): <c>count(u32 BE) + count * map</c>.
    /// Returns false (rather than throwing) if the payload does not look like this format,
    /// or if the decoded maps do not account for (almost) the entire payload.
    /// </summary>
    public static bool TryDecodeMapArray(byte[] payload, out IReadOnlyList<IReadOnlyDictionary<string, TaggedValue>> maps)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var result = new List<IReadOnlyDictionary<string, TaggedValue>>();
        maps = result;
        try
        {
            int pos = 0;
            if (payload.Length < 4) return false;
            uint count = ReadUInt32(payload, ref pos);
            if (count > 1000) return false; // implausible - not this format
            for (int i = 0; i < count; i++)
            {
                result.Add(DecodeMap(payload, ref pos));
            }
            // Require the parse to consume (almost) the entire payload - guards against
            // false-positive matches on unrelated binary data that happens to start with a
            // small plausible-looking count.
            int slack = payload.Length - pos;
            if (slack < 0 || slack > 4) { result.Clear(); return false; }
            return result.Count > 0;
        }
        catch (InvalidDataException)
        {
            result.Clear();
            return false;
        }
    }

    /// <summary>Decodes a single map starting at <paramref name="pos"/>, advancing it past the map.</summary>
    /// <exception cref="InvalidDataException">Thrown if the data does not match the expected format.</exception>
    public static Dictionary<string, TaggedValue> DecodeMap(byte[] data, ref int pos)
    {
        ArgumentNullException.ThrowIfNull(data);
        uint count = ReadUInt32(data, ref pos);
        if (count > 1000) throw new InvalidDataException($"Implausible map entry count {count} at position {pos}.");
        var map = new Dictionary<string, TaggedValue>((int)count);
        for (int i = 0; i < count; i++)
        {
            string key = DecodeString(data, ref pos) ?? throw new InvalidDataException($"Map entry {i} has a null key.");
            map[key] = DecodeValue(data, ref pos);
        }
        return map;
    }

    /// <summary>Decodes one tagged value (typeId + isNull + type-specific data) starting at <paramref name="pos"/>.</summary>
    /// <exception cref="InvalidDataException">Thrown for an unrecognized typeId, or malformed/truncated data.</exception>
    public static TaggedValue DecodeValue(byte[] data, ref int pos)
    {
        ArgumentNullException.ThrowIfNull(data);
        uint typeId = ReadUInt32(data, ref pos);
        RequireBytes(data, pos, 1, "isNull flag");
        bool isNull = data[pos] != 0;
        pos += 1;
        if (isNull) return TaggedValue.Null(typeId);

        return typeId switch
        {
            1 => TaggedValue.Of(ReadBytes(data, ref pos, 1)[0] != 0),
            2 => TaggedValue.Of(BinaryPrimitives.ReadInt32BigEndian(ReadBytes(data, ref pos, 4))),
            6 => TaggedValue.Of(BinaryPrimitives.ReadDoubleBigEndian(ReadBytes(data, ref pos, 8))),
            8 => TaggedValue.Of(DecodeMap(data, ref pos)),
            9 => TaggedValue.Of(DecodeList(data, ref pos)),
            10 => DecodeString(data, ref pos) is { } s ? TaggedValue.Of(s) : TaggedValue.Null(10),
            12 => DecodeByteArray(data, ref pos) is { } b ? TaggedValue.Of(b) : TaggedValue.Null(12),
            _ => throw new InvalidDataException(
                $"Unrecognized tagged-value typeId {typeId} at position {pos - 5}. " +
                "This codec deliberately does not guess at unknown types - extend DecodeValue/EncodeValue once the layout is confirmed."),
        };
    }

    /// <summary>Decodes a list (count-prefixed sequence of tagged values) starting at <paramref name="pos"/>.</summary>
    public static List<TaggedValue> DecodeList(byte[] data, ref int pos)
    {
        ArgumentNullException.ThrowIfNull(data);
        uint count = ReadUInt32(data, ref pos);
        if (count > 10_000) throw new InvalidDataException($"Implausible list entry count {count} at position {pos}.");
        var list = new List<TaggedValue>((int)count);
        for (int i = 0; i < count; i++) list.Add(DecodeValue(data, ref pos));
        return list;
    }

    /// <summary>
    /// Decodes a length-prefixed UTF-16BE string starting at <paramref name="pos"/>, or
    /// returns null if the length field is the null-string sentinel (0xFFFFFFFF).
    /// </summary>
    public static string? DecodeString(byte[] data, ref int pos)
    {
        ArgumentNullException.ThrowIfNull(data);
        uint byteLen = ReadUInt32(data, ref pos);
        if (byteLen == NullStringOrByteArrayLength) return null;
        if (byteLen == 0) return "";
        const uint maxPlausibleLength = 1024 * 1024;
        if (byteLen > maxPlausibleLength) throw new InvalidDataException($"Implausible string length {byteLen} at position {pos}.");
        RequireBytes(data, pos, (int)byteLen, "string data");
        var chars = new char[byteLen / 2];
        for (int i = 0; i < chars.Length; i++)
        {
            int b = pos + i * 2;
            chars[i] = (char)((data[b] << 8) | data[b + 1]);
        }
        pos += (int)byteLen;
        return new string(chars);
    }

    /// <summary>
    /// Decodes a length-prefixed raw byte array starting at <paramref name="pos"/>, or
    /// returns null if the length field is the null sentinel (0xFFFFFFFF).
    /// </summary>
    public static byte[]? DecodeByteArray(byte[] data, ref int pos)
    {
        ArgumentNullException.ThrowIfNull(data);
        uint byteLen = ReadUInt32(data, ref pos);
        if (byteLen == NullStringOrByteArrayLength) return null;
        const uint maxPlausibleLength = 64 * 1024 * 1024;
        if (byteLen > maxPlausibleLength) throw new InvalidDataException($"Implausible byte array length {byteLen} at position {pos}.");
        RequireBytes(data, pos, (int)byteLen, "byte array data");
        var bytes = data.AsSpan(pos, (int)byteLen).ToArray();
        pos += (int)byteLen;
        return bytes;
    }

    // ---- Encoding (byte-exact inverse of the Decode* methods above) ----

    /// <summary>Encodes a map to its wire representation.</summary>
    public static byte[] EncodeMap(IReadOnlyDictionary<string, TaggedValue> map)
    {
        using var stream = new MemoryStream();
        WriteMap(stream, map);
        return stream.ToArray();
    }

    public static void WriteMap(Stream stream, IReadOnlyDictionary<string, TaggedValue> map)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(map);
        WriteUInt32(stream, (uint)map.Count);
        foreach (var (key, value) in map)
        {
            WriteString(stream, key);
            WriteValue(stream, value);
        }
    }

    public static void WriteValue(Stream stream, TaggedValue value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        WriteUInt32(stream, value.TypeId);

        // CONFIRMED real-device behavior (observed in a real .Theme file's null "keyMacro"
        // field): for string (10) and byte-array (12) values, the outer isNull byte is
        // written as 0 (false) even when the value is logically null - nullability is
        // instead signaled by the string/byte-array's OWN length sentinel (0xFFFFFFFF).
        // For every other type, no real null example was observed on the wire; this codec
        // uses isNull=1 with no further data as the only sensible representation.
        if (value.TypeId is 10 or 12)
        {
            stream.WriteByte(0);
            if (value.TypeId == 10) WriteNullableString(stream, value.IsNull ? null : value.AsString);
            else WriteNullableByteArray(stream, value.IsNull ? null : value.AsByteArray);
            return;
        }

        stream.WriteByte(value.IsNull ? (byte)1 : (byte)0);
        if (value.IsNull) return;

        switch (value.TypeId)
        {
            case 1:
                stream.WriteByte((value.AsBool ?? throw new InvalidOperationException("TaggedValue.AsBool is null for a non-null bool value.")) ? (byte)1 : (byte)0);
                break;
            case 2:
                WriteInt32(stream, value.AsInt32 ?? throw new InvalidOperationException("TaggedValue.AsInt32 is null for a non-null int value."));
                break;
            case 6:
                WriteDouble(stream, value.AsDouble ?? throw new InvalidOperationException("TaggedValue.AsDouble is null for a non-null double value."));
                break;
            case 8:
                WriteMap(stream, value.AsMap ?? throw new InvalidOperationException("TaggedValue.AsMap is null for a non-null map value."));
                break;
            case 9:
                WriteList(stream, value.AsList ?? throw new InvalidOperationException("TaggedValue.AsList is null for a non-null list value."));
                break;
            case 10:
                WriteString(stream, value.AsString ?? throw new InvalidOperationException("TaggedValue.AsString is null for a non-null string value."));
                break;
            case 12:
                WriteByteArray(stream, value.AsByteArray ?? throw new InvalidOperationException("TaggedValue.AsByteArray is null for a non-null byte-array value."));
                break;
            default:
                throw new InvalidOperationException($"Cannot encode unrecognized tagged-value typeId {value.TypeId}.");
        }
    }

    public static void WriteList(Stream stream, IReadOnlyCollection<TaggedValue> list)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(list);
        WriteUInt32(stream, (uint)list.Count);
        foreach (var item in list) WriteValue(stream, item);
    }

    /// <summary>Writes a length-prefixed UTF-16BE string (never null - use <see cref="WriteValue"/> for a nullable tagged string).</summary>
    public static void WriteString(Stream stream, string value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(value);
        WriteUInt32(stream, (uint)(value.Length * 2));
        foreach (char c in value)
        {
            stream.WriteByte((byte)(c >> 8));
            stream.WriteByte((byte)(c & 0xFF));
        }
    }

    /// <summary>Writes a length-prefixed raw byte array (never null - use <see cref="WriteValue"/> for a nullable tagged byte array).</summary>
    public static void WriteByteArray(Stream stream, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(value);
        WriteUInt32(stream, (uint)value.Length);
        stream.Write(value);
    }

    /// <summary>Writes a length-prefixed UTF-16BE string, or the null-string sentinel (0xFFFFFFFF) if <paramref name="value"/> is null.</summary>
    private static void WriteNullableString(Stream stream, string? value)
    {
        if (value is null) { WriteUInt32(stream, NullStringOrByteArrayLength); return; }
        WriteString(stream, value);
    }

    /// <summary>Writes a length-prefixed raw byte array, or the null sentinel (0xFFFFFFFF) if <paramref name="value"/> is null.</summary>
    private static void WriteNullableByteArray(Stream stream, byte[]? value)
    {
        if (value is null) { WriteUInt32(stream, NullStringOrByteArrayLength); return; }
        WriteByteArray(stream, value);
    }

    /// <summary>Renders a decoded value as compact, human-readable JSON-like text for logging/diagnostics.</summary>
    public static string ToDisplayString(TaggedValue value)
    {
        var sb = new StringBuilder();
        AppendDisplay(sb, value);
        return sb.ToString();
    }

    /// <summary>Renders a decoded map array (see <see cref="TryDecodeMapArray"/>) as compact JSON-like text.</summary>
    public static string ToDisplayString(IReadOnlyList<IReadOnlyDictionary<string, TaggedValue>> maps)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < maps.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            AppendDisplayMap(sb, maps[i]);
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static void AppendDisplay(StringBuilder sb, TaggedValue value)
    {
        if (value.IsNull) { sb.Append("null"); return; }
        switch (value.TypeId)
        {
            case 1: sb.Append(value.AsBool!.Value ? "true" : "false"); break;
            case 2: sb.Append(value.AsInt32); break;
            case 6: sb.Append(value.AsDouble); break;
            case 8: AppendDisplayMap(sb, value.AsMap!); break;
            case 9:
                sb.Append('[');
                for (int i = 0; i < value.AsList!.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    AppendDisplay(sb, value.AsList[i]);
                }
                sb.Append(']');
                break;
            case 10:
                string s = value.AsString!;
                if (s.Length > 300) s = s[..300] + $"...<+{s.Length - 300} chars>";
                sb.Append('"').Append(s).Append('"');
                break;
            case 12: sb.Append($"<{value.AsByteArray!.Length} raw bytes>"); break;
            default: sb.Append("<unrecognized>"); break;
        }
    }

    private static void AppendDisplayMap(StringBuilder sb, IReadOnlyDictionary<string, TaggedValue> map)
    {
        sb.Append('{');
        bool first = true;
        foreach (var (key, value) in map)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append('"').Append(key).Append("\": ");
            AppendDisplay(sb, value);
        }
        sb.Append('}');
    }

    private static uint ReadUInt32(byte[] data, ref int pos)
    {
        RequireBytes(data, pos, 4, "u32 field");
        uint v = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
        pos += 4;
        return v;
    }

    private static byte[] ReadBytes(byte[] data, ref int pos, int n)
    {
        RequireBytes(data, pos, n, "fixed-size field");
        var slice = data.AsSpan(pos, n).ToArray();
        pos += n;
        return slice;
    }

    private static void RequireBytes(byte[] data, int pos, int count, string what)
    {
        if (pos < 0 || pos + count > data.Length)
            throw new InvalidDataException($"Truncated data: expected {count} more byte(s) for {what} at position {pos}, but only {Math.Max(0, data.Length - pos)} remain.");
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteDouble(Stream stream, double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        stream.Write(buffer);
    }
}
