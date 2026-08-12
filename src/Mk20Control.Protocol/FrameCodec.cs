using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Mk20Control.Protocol;

/// <summary>
/// Raw frame magic + fields per PROTOCOL_WAVESHARE_MK20.md section 3 ("Frame format [VERIFIED]"):
///
///   offset  size  field
///   0       4     magic       A1 A5 5A 5E
///   4       4     id (u32)
///   8       4     cmd (u32)
///   12      4     size (u32)             payload length
///   16      4     size_crc (u32)         crc32(the 4 size bytes)
///   20      size  payload
///   20+size 4     data_crc (u32)         crc32(payload)
///
/// All integers little-endian.
/// </summary>
public sealed record Mk20Frame(uint Id, uint Cmd, byte[] Payload)
{
    public static readonly byte[] Magic = { 0xA1, 0xA5, 0x5A, 0x5E };

    public byte[] Encode()
    {
        int size = Payload.Length;
        var buffer = new byte[20 + size + 4];

        Magic.CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), Id);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), Cmd);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), (uint)size);

        uint sizeCrc = Crc32.Compute(buffer.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16, 4), sizeCrc);

        Payload.CopyTo(buffer, 20);

        uint dataCrc = Crc32.Compute(Payload);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20 + size, 4), dataCrc);

        return buffer;
    }
}

/// <summary>
/// Incremental frame reassembler for a byte stream. Resyncs to the next magic on a bad
/// size_crc rather than blindly skipping 24 bytes (the doc explicitly recommends this,
/// since the vendor parser's skip-24 behavior can discard a valid frame following corruption).
/// </summary>
public sealed class Mk20FrameParser
{
    private readonly List<byte> _buffer = new();

    public void Feed(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data.ToArray());
    }

    /// <summary>Extracts as many complete, valid frames as currently available.</summary>
    public IEnumerable<Mk20Frame> DrainFrames()
    {
        while (true)
        {
            int magicIndex = FindMagic();
            if (magicIndex < 0)
            {
                // No magic at all: keep at most 3 trailing bytes (partial magic candidate).
                if (_buffer.Count > 3)
                {
                    _buffer.RemoveRange(0, _buffer.Count - 3);
                }
                yield break;
            }

            if (magicIndex > 0)
            {
                _buffer.RemoveRange(0, magicIndex);
            }

            if (_buffer.Count < 20)
            {
                yield break; // header incomplete, wait for more bytes
            }

            var header = _buffer.GetRange(0, 20).ToArray();
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
            uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
            uint sizeCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4));

            uint expectedSizeCrc = Crc32.Compute(header.AsSpan(12, 4));
            if (sizeCrc != expectedSizeCrc)
            {
                // Corrupt length field - resync to the *next* magic instead of trusting `size`.
                _buffer.RemoveRange(0, 4); // drop this magic, search again past it
                continue;
            }

            long total = 20L + size + 4;
            if (total > int.MaxValue || _buffer.Count < total)
            {
                yield break; // not enough data yet (or absurd size - wait/resync will occur naturally)
            }

            var payload = _buffer.GetRange(20, (int)size).ToArray();
            var dataCrcBytes = _buffer.GetRange(20 + (int)size, 4).ToArray();
            uint dataCrc = BinaryPrimitives.ReadUInt32LittleEndian(dataCrcBytes);
            uint expectedDataCrc = Crc32.Compute(payload);

            _buffer.RemoveRange(0, (int)total);

            if (dataCrc != expectedDataCrc)
            {
                // Payload corrupt; drop and keep scanning rather than surfacing bad data.
                continue;
            }

            yield return new Mk20Frame(id, cmd, payload);
        }
    }

    private int FindMagic()
    {
        var magic = Mk20Frame.Magic;
        for (int i = 0; i <= _buffer.Count - magic.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < magic.Length; j++)
            {
                if (_buffer[i + j] != magic[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
