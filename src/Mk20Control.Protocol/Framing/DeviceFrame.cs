using System;
using System.Buffers.Binary;
using System.Text;
using Mk20Control.Protocol.Checksums;

using Mk20Control.Protocol.Compat;

namespace Mk20Control.Protocol.Framing;

/// <summary>
/// Byte-level constants for the confirmed real MK20 wire frame, reverse-engineered from a
/// live USBPcap capture of the vendor ScreenKeyWindows app talking to a physical MK20
/// (device VID:PID 1d6b:0104, bulk endpoints 0x01 OUT / 0x81 IN, CDC-ACM).
///
/// Frame layout (all integer fields little-endian):
///
///   offset  size  field
///   0       22    ASCII literal "AA551234 FIXEDCMDHEAD " (with trailing space)
///   22      4     packetType (u32) - see <see cref="Model.PacketType"/>
///   26      4     commandId (u32) - see <see cref="Model.CommandId"/>
///   30      4     payloadLength (u32)
///   34      4     payloadCrc32 (u32) - zlib CRC-32 of the payload
///   38      payloadLength  payload
///
/// There is also a separate, non-length-prefixed literal control message observed on the
/// wire: "AA551234 Abort file transfer 123455AA" (fixed ASCII string, no binary payload).
/// </summary>
public static class DeviceFrameHeader
{
    public const string CommandHeaderText = "AA551234 FIXEDCMDHEAD ";
    public static readonly byte[] CommandHeaderBytes = Encoding.ASCII.GetBytes(CommandHeaderText);

    public const string AbortTransferText = "AA551234 Abort file transfer 123455AA";
    public static readonly byte[] AbortTransferBytes = Encoding.ASCII.GetBytes(AbortTransferText);

    public const string SyncMagicText = "AA551234";
    public static readonly byte[] SyncMagicBytes = Encoding.ASCII.GetBytes(SyncMagicText);

    /// <summary>Total header length in bytes: 22 (ASCII prefix) + 4*4 (u32 fields).</summary>
    public const int HeaderLength = 38;

    /// <summary>
    /// Sentinel command id used by <see cref="DeviceFrame"/> to represent the standalone
    /// "Abort file transfer" control message, which carries no command id or payload of its
    /// own on the wire.
    /// </summary>
    public const uint AbortTransferCommandId = uint.MaxValue;
}

/// <summary>
/// A single decoded (or to-be-encoded) real MK20 wire frame. See <see cref="DeviceFrameHeader"/>
/// for the exact byte layout this represents.
/// </summary>
/// <param name="PacketType">0 = request (host to device), 2 = ack/reply (device to host). See <see cref="Model.PacketType"/>.</param>
/// <param name="CommandId">The command identifier. See <see cref="Model.CommandId"/>.</param>
/// <param name="Payload">The raw, un-decoded payload bytes.</param>
/// <param name="DeclaredChecksum">The CRC-32 value declared in the frame header.</param>
/// <param name="IsChecksumValid">Whether <see cref="DeclaredChecksum"/> matches the actual CRC-32 of <paramref name="Payload"/>.</param>
public sealed record DeviceFrame(uint PacketType, uint CommandId, byte[] Payload, uint DeclaredChecksum, bool IsChecksumValid)
{
    /// <summary>True if this frame represents the standalone "Abort file transfer" control message rather than a normal command frame.</summary>
    public bool IsAbortTransferMessage => CommandId == DeviceFrameHeader.AbortTransferCommandId;

    /// <summary>Encodes this frame to its on-wire byte representation, recomputing the payload checksum.</summary>
    /// <exception cref="InvalidOperationException">Thrown if this frame represents the abort-transfer sentinel, which has no standard frame encoding.</exception>
    public byte[] Encode()
    {
        if (IsAbortTransferMessage)
            throw new InvalidOperationException(
                "Cannot Encode() the abort-transfer sentinel frame; write DeviceFrameHeader.AbortTransferBytes directly instead.");

        var buffer = new byte[DeviceFrameHeader.HeaderLength + Payload.Length];
        DeviceFrameHeader.CommandHeaderBytes.CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(22, 4), PacketType);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(26, 4), CommandId);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(30, 4), (uint)Payload.Length);
        uint crc = Crc32.Compute(Payload);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(34, 4), crc);
        Payload.CopyTo(buffer, DeviceFrameHeader.HeaderLength);
        return buffer;
    }

    /// <summary>Constructs a well-formed request frame (packetType=0) ready to encode and send to the device.</summary>
    public static DeviceFrame CreateRequest(uint commandId, byte[] payload)
    {
        Guard.NotNull(payload);
        uint crc = Crc32.Compute(payload);
        return new DeviceFrame(PacketType: 0, commandId, payload, crc, IsChecksumValid: true);
    }

    /// <summary>The reusable sentinel representing the standalone "Abort file transfer" control message.</summary>
    public static readonly DeviceFrame AbortTransferMessage =
        new(0, DeviceFrameHeader.AbortTransferCommandId, Array.Empty<byte>(), 0, true);
}
