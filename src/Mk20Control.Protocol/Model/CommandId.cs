namespace Mk20Control.Protocol.Model;

/// <summary>
/// Command identifiers for the confirmed real MK20 wire protocol (see
/// <see cref="Mk20Control.Protocol.Framing.DeviceFrameHeader"/>). Each member's XML doc
/// states its confirmation level plainly - several values (5, 8, 9, 10, 11, 14) are only
/// "ordering-inferred" from the vendor's firmware/EXE string ordering and have NOT been
/// individually observed on the wire; treat those as experimental until confirmed.
/// </summary>
public enum CommandId : uint
{
    /// <summary>CONFIRMED: a zero-length ping/keepalive payload was observed for this command.</summary>
    FindDevice = 0,

    /// <summary>CONFIRMED: carries a length-prefixed string key/value map (see <see cref="Codecs.SystemDataCodec"/>).</summary>
    SendSystemDataToDevice = 1,

    /// <summary>CONFIRMED: carries a plain UTF-8 device-side theme path with no length prefix (e.g. "/data/theme/MK20/&lt;name&gt;/&lt;name&gt;.Theme").</summary>
    SetDeviceReload = 2,

    /// <summary>CONFIRMED: device reply lists installed themes as (path, crc32) pairs plus free-space fields.</summary>
    GetDeviceTheme = 3,

    /// <summary>CONFIRMED: payload is the brightness level as ASCII decimal text (e.g. "99", "100"), no binary encoding.</summary>
    SetDeviceBacklight = 4,

    /// <summary>Ordering-inferred only - not yet individually confirmed on the wire.</summary>
    SetDeviceScanState = 5,

    /// <summary>CONFIRMED: carries a file path and total size (Simple String Map, {path: totalSize}) when starting a theme file upload. The device replies with an empty payload; the raw file bytes are then written directly to the transport in fixed 4096-byte chunks with no additional framing (see <see cref="Client.Mk20DeviceClient.UploadThemeFileAsync"/>).</summary>
    FileStart = 6,

    /// <summary>CONFIRMED: carries a file path and CRC-32 as decimal text (Simple String Map, {path: crc32AsText}, host-&gt;device) or {"res":"1","fileName":path} (device-&gt;host) when finishing a theme file upload.</summary>
    FileEnd = 7,

    /// <summary>Ordering-inferred only - not yet individually confirmed on the wire.</summary>
    GetDeviceVersion = 8,

    /// <summary>Ordering-inferred only - not yet individually confirmed on the wire.</summary>
    SetDeviceCanvasFlip = 9,

    /// <summary>Ordering-inferred only - not yet individually confirmed on the wire.</summary>
    GetDeviceScreenMessage = 10,

    /// <summary>CONFIRMED: request payload is a Simple String Map (see <see cref="Codecs.SimpleStringMapCodec"/>) with one entry, the device-side theme path mapped to an empty string value: {path: ""}. Reply is {"res":"1"} on success.</summary>
    SetDeviceDeleteTheme = 11,

    /// <summary>CONFIRMED: observed wrapping a JPEG a few bytes after a "ScreenKey" tagged-map key (430 occurrences in one capture); the exact wrapping tagged-value type was not fully decoded, so only receiving/detecting this command is supported, not sending it.</summary>
    SendPixmap = 12,

    /// <summary>CONFIRMED: carries a structured tagged-value array-of-maps event payload (see <see cref="Codecs.VariantMapCodec.TryDecodeMapArray"/>) - fired for key presses/releases and encoder-assignment notifications that have a bound action.</summary>
    DeviceProactiveEscalationCommand = 13,

    /// <summary>Ordering-inferred only - not yet individually confirmed on the wire.</summary>
    RequestUploadKey = 14,

    /// <summary>CONFIRMED: carries UTF-8 JSON text (e.g. {"connect":true}, deviceRequestSystemData proactive-escalation messages).</summary>
    SendJson = 15,
}

/// <summary>
/// The "packetType" header field of a real MK20 wire frame. CONFIRMED values from capture:
/// 0 = request (host->device), 2 = ack/reply (device->host). Values 1 and 3 have not been
/// observed.
/// </summary>
public enum PacketType : uint
{
    Request = 0,
    AckReply = 2,
}
