using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Mk20Control.Protocol.Checksums;

namespace Mk20Control.Protocol.Framing;

/// <summary>
/// Incremental, resynchronizing parser for the real MK20 wire frame format (see
/// <see cref="DeviceFrameHeader"/>). Feed it raw bytes as they arrive from the transport
/// (in any chunk size) and call <see cref="DrainFrames"/> to extract every frame that has
/// become fully available so far.
///
/// Resync behavior: if a length field looks implausible, or a recognized header prefix is
/// not present after a magic match, the parser discards just the matched magic bytes and
/// keeps scanning forward, rather than assuming any particular fixed frame size - this
/// avoids losing a valid frame that happens to follow corrupted/partial data.
/// </summary>
public sealed class DeviceFrameParser
{
    private readonly List<byte> _buffer = new();

    /// <summary>Appends newly received bytes to the internal buffer.</summary>
    public void Feed(ReadOnlySpan<byte> data) => _buffer.AddRange(data.ToArray());

    /// <summary>Extracts every frame that is fully available in the buffer so far.</summary>
    public IEnumerable<DeviceFrame> DrainFrames()
    {
        while (true)
        {
            int magicIndex = IndexOf(DeviceFrameHeader.SyncMagicBytes);
            if (magicIndex < 0)
            {
                // No magic present at all: retain only enough trailing bytes to catch a
                // magic that arrives split across two Feed() calls.
                if (_buffer.Count > DeviceFrameHeader.SyncMagicBytes.Length)
                    _buffer.RemoveRange(0, _buffer.Count - DeviceFrameHeader.SyncMagicBytes.Length + 1);
                yield break;
            }
            if (magicIndex > 0) _buffer.RemoveRange(0, magicIndex);

            if (StartsWith(DeviceFrameHeader.AbortTransferBytes))
            {
                _buffer.RemoveRange(0, DeviceFrameHeader.AbortTransferBytes.Length);
                yield return DeviceFrame.AbortTransferMessage;
                continue;
            }

            if (!StartsWith(DeviceFrameHeader.CommandHeaderBytes))
            {
                // Could be a partial match at the buffer tail (wait for more data) or
                // genuinely unrecognized framing (resync past this magic and keep scanning).
                if (_buffer.Count < DeviceFrameHeader.CommandHeaderBytes.Length) yield break;
                _buffer.RemoveRange(0, DeviceFrameHeader.SyncMagicBytes.Length);
                continue;
            }

            if (_buffer.Count < DeviceFrameHeader.HeaderLength) yield break; // wait for the full header

            var header = _buffer.GetRange(0, DeviceFrameHeader.HeaderLength).ToArray();
            uint packetType = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(22, 4));
            uint commandId = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(26, 4));
            uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(30, 4));
            uint declaredChecksum = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(34, 4));

            long totalFrameLength = DeviceFrameHeader.HeaderLength + (long)payloadLength;
            const long maxPlausiblePayload = 8 * 1024 * 1024;
            if (payloadLength > maxPlausiblePayload || totalFrameLength > int.MaxValue)
            {
                // Implausible length field - resync past this magic instead of trusting it.
                _buffer.RemoveRange(0, DeviceFrameHeader.SyncMagicBytes.Length);
                continue;
            }
            if (_buffer.Count < totalFrameLength) yield break; // wait for the rest of the payload

            var payload = _buffer.GetRange(DeviceFrameHeader.HeaderLength, (int)payloadLength).ToArray();
            _buffer.RemoveRange(0, (int)totalFrameLength);

            bool checksumValid = Crc32.Compute(payload) == declaredChecksum;
            yield return new DeviceFrame(packetType, commandId, payload, declaredChecksum, checksumValid);
        }
    }

    private bool StartsWith(byte[] pattern)
    {
        if (_buffer.Count < pattern.Length) return _buffer.Count > 0 && MatchesAvailablePrefix(pattern);
        for (int i = 0; i < pattern.Length; i++)
            if (_buffer[i] != pattern[i]) return false;
        return true;
    }

    private bool MatchesAvailablePrefix(byte[] pattern)
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
