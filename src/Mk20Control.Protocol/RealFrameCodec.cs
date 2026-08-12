using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Mk20Control.Protocol;

/// <summary>
/// The REAL wire framing observed on live hardware via USBPcap + Wireshark capture of the
/// vendor ScreenKeyWindows app talking to a physical MK20 (device VID:PID 1d6b:0104, bulk
/// endpoints 0x01 OUT / 0x81 IN). This supersedes the A1A55A5E binary framing guessed in
/// PROTOCOL_WAVESHARE_MK20.md section 3, which does NOT appear anywhere in real traffic for
/// this firmware/app version.
///
/// Frame layout (all integers little-endian):
///
///   offset  size  field
///   0       22    ASCII literal "AA551234 FIXEDCMDHEAD " (with trailing space)
///   22      4     packetType (u32)   0 = request (host->device), 2 = ack/reply (device->host)
///   26      4     cmd (u32)          CMD_VALUE - see below, CONFIRMED against capture
///   30      4     payloadLen (u32)
///   34      4     payloadCrc (u32)   zlib crc32 of the payload
///   38      payloadLen  payload
///
/// CONFIRMED CMD_VALUE numbers (from this capture, cross-checked against the doc's guessed
/// order in CmdValue.cs, which turned out to match exactly):
///   0  = FIND_DEVICE               (zero-length ping/keepalive payload observed)
///   1  = SEND_SYSTEM_DATA_TO_DEVICE (payload = custom length-prefixed UTF-16 key/value map)
///   15 = SEND_JSON                  (payload = UTF-8 JSON text, e.g. getInfo-style replies,
///                                    deviceRequestSystemData proactive-escalation, etc.)
///
/// There is also a separate, non-length-prefixed literal control message observed:
///   "AA551234 Abort file transfer 123455AA" (fixed ASCII string, no binary payload) -
/// matching the doc's section 10.2 firmware-transfer-abort note almost exactly (the doc
/// guessed a raw-byte 0xAA 0x55 0x12 0x34 magic; on real hardware it is literal ASCII text).
/// </summary>
public static class RealFrameHeader
{
    public const string CmdHeaderText = "AA551234 FIXEDCMDHEAD ";
    public static readonly byte[] CmdHeaderBytes = Encoding.ASCII.GetBytes(CmdHeaderText);

    public const string AbortLiteralText = "AA551234 Abort file transfer 123455AA";
    public static readonly byte[] AbortLiteralBytes = Encoding.ASCII.GetBytes(AbortLiteralText);

    public const string MagicText = "AA551234";
    public static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes(MagicText);

    public const int HeaderLength = 38; // 22 (ascii) + 4*4 (u32 fields)
}

public sealed record RealFrame(uint PacketType, uint Cmd, byte[] Payload, uint DeclaredCrc, bool CrcOk)
{
    public byte[] Encode()
    {
        var buffer = new byte[RealFrameHeader.HeaderLength + Payload.Length];
        RealFrameHeader.CmdHeaderBytes.CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(22, 4), PacketType);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(26, 4), Cmd);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(30, 4), (uint)Payload.Length);
        uint crc = Crc32.Compute(Payload);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(34, 4), crc);
        Payload.CopyTo(buffer, 38);
        return buffer;
    }
}

/// <summary>
/// Scans a byte stream for "AA551234" markers and decodes either a structured
/// " FIXEDCMDHEAD " frame or recognizes the literal "Abort file transfer" control string.
/// Unlike <see cref="Mk20FrameParser"/> (for the A1A55A5E doc-guessed framing), this is built
/// directly from observed hardware traffic.
/// </summary>
public sealed class RealFrameParser
{
    private readonly List<byte> _buffer = new();

    public void Feed(ReadOnlySpan<byte> data) => _buffer.AddRange(data.ToArray());

    public IEnumerable<RealFrame> DrainFrames()
    {
        while (true)
        {
            int magicIndex = IndexOf(RealFrameHeader.MagicBytes);
            if (magicIndex < 0)
            {
                if (_buffer.Count > RealFrameHeader.MagicBytes.Length)
                    _buffer.RemoveRange(0, _buffer.Count - RealFrameHeader.MagicBytes.Length + 1);
                yield break;
            }
            if (magicIndex > 0) _buffer.RemoveRange(0, magicIndex);

            // Try the literal "Abort file transfer" control string first.
            if (StartsWith(RealFrameHeader.AbortLiteralBytes))
            {
                _buffer.RemoveRange(0, RealFrameHeader.AbortLiteralBytes.Length);
                yield return new RealFrame(0, uint.MaxValue, Array.Empty<byte>(), 0, true); // sentinel cmd for "abort"
                continue;
            }

            if (!StartsWith(RealFrameHeader.CmdHeaderBytes))
            {
                // Not a recognized sub-header yet; could be a partial match at the buffer
                // tail, or genuinely unknown framing. Drop the magic and keep scanning.
                if (_buffer.Count < RealFrameHeader.CmdHeaderBytes.Length) yield break; // wait for more data
                _buffer.RemoveRange(0, RealFrameHeader.MagicBytes.Length);
                continue;
            }

            if (_buffer.Count < RealFrameHeader.HeaderLength) yield break; // wait for full header

            var header = _buffer.GetRange(0, RealFrameHeader.HeaderLength).ToArray();
            uint packetType = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(22, 4));
            uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(26, 4));
            uint payloadLen = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(30, 4));
            uint declaredCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(34, 4));

            long total = RealFrameHeader.HeaderLength + (long)payloadLen;
            if (payloadLen > 8 * 1024 * 1024 || total > int.MaxValue)
            {
                // Implausible length - resync past this magic instead of trusting it.
                _buffer.RemoveRange(0, RealFrameHeader.MagicBytes.Length);
                continue;
            }
            if (_buffer.Count < total) yield break; // wait for more data

            var payload = _buffer.GetRange(RealFrameHeader.HeaderLength, (int)payloadLen).ToArray();
            _buffer.RemoveRange(0, (int)total);

            bool crcOk = Crc32.Compute(payload) == declaredCrc;
            yield return new RealFrame(packetType, cmd, payload, declaredCrc, crcOk);
        }
    }

    private bool StartsWith(byte[] pattern)
    {
        if (_buffer.Count < pattern.Length) return _buffer.Count > 0 && MatchesPrefix(pattern);
        for (int i = 0; i < pattern.Length; i++)
            if (_buffer[i] != pattern[i]) return false;
        return true;
    }

    private bool MatchesPrefix(byte[] pattern)
    {
        int n = Math.Min(_buffer.Count, pattern.Length);
        for (int i = 0; i < n; i++)
            if (_buffer[i] != pattern[i]) return false;
        return true;
    }

    private int IndexOf(byte[] pattern)
    {
        for (int i = 0; i <= _buffer.Count - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (_buffer[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}

/// <summary>
/// Decodes the SEND_SYSTEM_DATA_TO_DEVICE (cmd=1) payload: a Qt QDataStream-serialized
/// QMap&lt;QString,QString&gt;. QDataStream defaults to big-endian, so the outer count and
/// each QString's byte-length prefix are BIG-ENDIAN (unlike the frame header's u32 fields,
/// which are little-endian - two different serializers are in play: the outer envelope and
/// the inner Qt-serialized payload). Observed on real hardware pushing values like
/// "GPU Usage" -> "0%", "CPU Usage" -> "21%".
/// </summary>
public static class SystemDataCodec
{
    public static List<KeyValuePair<string, string>> Decode(byte[] payload)
    {
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

    private static string? ReadLengthPrefixedUtf16(byte[] payload, ref int pos)
    {
        if (pos + 4 > payload.Length) return null;
        uint byteLen = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(pos, 4));
        pos += 4;
        if (byteLen == 0) return "";
        if (pos + byteLen > payload.Length) return null;
        // Observed as big-endian UTF-16 code units (Qt QDataStream default for QString: BE).
        var chars = new char[byteLen / 2];
        for (int i = 0; i < chars.Length; i++)
        {
            int b = pos + i * 2;
            chars[i] = (char)((payload[b] << 8) | payload[b + 1]);
        }
        pos += (int)byteLen;
        return new string(chars);
    }
}

/// <summary>
/// Best-effort heuristic extractor for the various other cmd payloads observed on the wire
/// (FILE_START, FILE_END, GET_DEVICE_THEME, SET_DEVICE_RELOAD, GET_DEVICE_VERSION, ...),
/// which all appear to reuse the same Qt QDataStream "u32 BE length + UTF-16BE chars" string
/// encoding as SEND_SYSTEM_DATA_TO_DEVICE, but interleaved with other binary fields (numeric
/// values, nested structures) whose exact per-cmd schema hasn't been fully reverse engineered
/// yet. This scans for plausible (length, printable-UTF16BE-text) tokens anywhere in the
/// payload so field names/paths/values (e.g. "fileName", "bytesTotal", a theme path) show up
/// in the analyzer output instead of only opaque hex.
/// </summary>
public static class QtBlobBestEffortDecoder
{
    public static List<string> ExtractStrings(byte[] payload)
    {
        var found = new List<string>();
        int pos = 0;
        while (pos + 4 <= payload.Length)
        {
            uint len = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(pos, 4));
            if (len > 0 && len % 2 == 0 && len <= 1024 && pos + 4 + len <= payload.Length)
            {
                var chars = new char[len / 2];
                bool printable = true;
                for (int i = 0; i < chars.Length; i++)
                {
                    int b = pos + 4 + i * 2;
                    char c = (char)((payload[b] << 8) | payload[b + 1]);
                    if (c != 0 && (c < 0x20 || c > 0x7E) && c != '\u00E6' /* allow a few common non-ascii, best effort */)
                    {
                        // Allow common CJK ranges too (theme names observed in Chinese).
                        if (!(c >= 0x4E00 && c <= 0x9FFF)) { printable = false; break; }
                    }
                    chars[i] = c;
                }
                if (printable)
                {
                    string s = new string(chars).TrimEnd('\0');
                    if (s.Length > 0)
                    {
                        found.Add(s);
                        pos += 4 + (int)len;
                        continue;
                    }
                }
            }
            pos++;
        }
        return found;
    }
}

