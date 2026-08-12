namespace Mk20Control.Protocol;

/// <summary>
/// CMD_VALUE constants. Layer-B (SHOW_JPG=100/JSON=101/END=102) come from the readable Qt
/// demo source (PROTOCOL_WAVESHARE_MK20.md section 4) and describe the OpenSourceLicenseDemo
/// app's own protocol - they are NOT used by the real ScreenKeyWindows/MK20 wire traffic.
///
/// The Layer-A values below (FindDevice=0 ... SendJson=15) were CONFIRMED against a live
/// USBPcap capture of the vendor ScreenKeyWindows app talking to a physical MK20 (see
/// RealFrameCodec.cs): cmd=1 was observed carrying SEND_SYSTEM_DATA_TO_DEVICE-shaped
/// key/value payloads (e.g. "GPU Usage"="0%"), and cmd=15 was observed carrying SEND_JSON
/// payloads (getInfo-style JSON, deviceRequestSystemData proactive escalation, etc). The
/// remaining values (2-14) follow the same firmware enum ordering documented in
/// PROTOCOL_WAVESHARE_MK20.md section 4 and are very likely correct by the same pattern,
/// but have not each been individually confirmed on the wire yet.
/// </summary>
public static class CmdValue
{
    // ---- Layer B ("open" demo protocol, OpenSourceLicenseDemo only) - VERIFIED byte-exact for that app ----
    public const uint ShowJpg = 100;
    public const uint Json = 101;
    public const uint End = 102;

    // ---- Layer A ("Full ScreenKey" retail protocol) - real wire framing, see RealFrameCodec.cs ----
    public const uint FindDevice = 0;                    // CONFIRMED: zero-length ping/keepalive observed
    public const uint SendSystemDataToDevice = 1;        // CONFIRMED: key/value system_data payload observed
    public const uint SetDeviceReload = 2;                // ordering-inferred, not yet individually confirmed
    public const uint GetDeviceTheme = 3;                 // ordering-inferred, not yet individually confirmed
    public const uint SetDeviceBacklight = 4;             // ordering-inferred, not yet individually confirmed
    public const uint SetDeviceScanState = 5;             // ordering-inferred, not yet individually confirmed
    public const uint FileStart = 6;                      // ordering-inferred, not yet individually confirmed
    public const uint FileEnd = 7;                        // ordering-inferred, not yet individually confirmed
    public const uint GetDeviceVersion = 8;               // ordering-inferred, not yet individually confirmed
    public const uint SetDeviceCanvasFlip = 9;            // ordering-inferred, not yet individually confirmed
    public const uint GetDeviceScreenMessage = 10;        // ordering-inferred, not yet individually confirmed
    public const uint SetDeviceDeleteTheme = 11;          // ordering-inferred, not yet individually confirmed
    public const uint SendPixmap = 12;                    // ordering-inferred, not yet individually confirmed
    public const uint DeviceProactiveEscalationCmd = 13;  // ordering-inferred, not yet individually confirmed
    public const uint RequestUploadKey = 14;              // ordering-inferred, not yet individually confirmed
    public const uint SendJson = 15;                      // CONFIRMED: JSON payloads observed at cmd=15
}

/// <summary>
/// DATA_PACKET_TYPE / "packetType" field observed in the real frame header (see
/// RealFrameCodec.cs). CONFIRMED values from capture: 0 = request (host->device),
/// 2 = ack/reply (device->host). 1 and 3 (doc's guessed File/FileAck) not yet observed.
/// </summary>
public enum DataPacketType : uint
{
    Cmd = 0,
    File = 1,
    CmdAck = 2,
    FileAck = 3,
}
