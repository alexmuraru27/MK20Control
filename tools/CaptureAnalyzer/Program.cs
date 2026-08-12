using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Mk20Control.Protocol;

// Decodes MK20 traffic out of a Wireshark/USBPcap capture of the device's USB CDC-ACM
// bulk endpoints.
//
// IMPORTANT: the real wire format observed on hardware (see RealFrameCodec.cs) is NOT the
// A1A55A5E binary framing guessed in PROTOCOL_WAVESHARE_MK20.md section 3. It's an ASCII
// header "AA551234 FIXEDCMDHEAD " followed by 4 little-endian u32 fields (packetType, cmd,
// payloadLen, payloadCrc) and then the payload. This tool decodes that real framing by
// default; use --legacy-a1a55a5e to try the old doc-guessed framing instead.
//
// Typical workflow:
//   1. Capture on the USBPcap interface while running the vendor ScreenKeyWindows app and
//      pressing keys / loading a theme / setting a picture.
//   2. Save as a .pcapng file.
//   3. dotnet run --project tools\CaptureAnalyzer -- <path-to-capture.pcapng> [tshark-path]
//
// This shells out to `tshark` (bundled with Wireshark), auto-detects the MK20's USB device
// address by matching known VIDs (1d6b:0104 / 1234:5678 per the doc), pulls the CDC-ACM
// in/out payload bytes (usbcom.data.in_payload / usbcom.data.out_payload fields), and feeds
// each direction's concatenated byte stream through RealFrameParser.

if (args.Length >= 1 && args[0] == "--selftest")
{
    return RunSelfTest();
}

if (args.Length < 1)
{
    Console.WriteLine("Usage: CaptureAnalyzer <capture.pcapng> [path-to-tshark.exe] [--legacy-a1a55a5e] [--device-address=N]");
    Console.WriteLine(@"Default tshark path tried: C:\Program Files\Wireshark\tshark.exe");
    Console.WriteLine("       CaptureAnalyzer --selftest   (verifies the frame encode/decode pipeline with synthetic data, no capture needed)");
    return 1;
}

string capturePath = args[0];
if (!File.Exists(capturePath))
{
    Console.WriteLine($"File not found: {capturePath}");
    return 1;
}

bool useLegacy = args.Contains("--legacy-a1a55a5e");
int? forcedDeviceAddress = args.FirstOrDefault(a => a.StartsWith("--device-address="))
    is { } da ? int.Parse(da.Split('=')[1]) : null;

string? tsharkArg = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
string tsharkPath = tsharkArg ?? FindTshark();
if (!File.Exists(tsharkPath))
{
    Console.WriteLine($"tshark not found at '{tsharkPath}'. Pass its path as the 2nd argument.");
    return 1;
}

int deviceAddress = forcedDeviceAddress ?? FindMk20DeviceAddress(tsharkPath, capturePath);
if (deviceAddress < 0)
{
    Console.WriteLine("Could not auto-detect the MK20's USB device address (expected VID 0x1d6b/PID 0x0104 " +
                       "or VID 0x1234/PID 0x5678, per PROTOCOL_WAVESHARE_MK20.md). Pass --device-address=N manually.");
    return 1;
}
Console.WriteLine($"MK20 USB device address: {deviceAddress}");

var rows = useLegacy
    ? RunTsharkLegacyCapdata(tsharkPath, capturePath, deviceAddress)
    : RunTsharkUsbcom(tsharkPath, capturePath, deviceAddress);
Console.WriteLine($"{rows.Count} USB packets with payload data found for device {deviceAddress}.");

var hostToDevice = new List<byte>();
var deviceToHost = new List<byte>();
foreach (var row in rows.OrderBy(r => r.FrameNumber))
{
    var bytes = HexToBytes(row.CapData);
    if (row.DirectionIn) deviceToHost.AddRange(bytes);
    else hostToDevice.AddRange(bytes);
}

Console.WriteLine();
Console.WriteLine("=== host -> device (OUT) ===");
if (useLegacy) DecodeLegacyAndPrint(hostToDevice.ToArray(), "H>D");
else DecodeRealAndPrint(hostToDevice.ToArray(), "H>D");

Console.WriteLine();
Console.WriteLine("=== device -> host (IN) ===");
if (useLegacy) DecodeLegacyAndPrint(deviceToHost.ToArray(), "D>H");
else DecodeRealAndPrint(deviceToHost.ToArray(), "D>H");

return 0;

static string FindTshark()
{
    string[] candidates =
    {
        @"C:\Program Files\Wireshark\tshark.exe",
        @"C:\Program Files (x86)\Wireshark\tshark.exe",
    };
    foreach (var c in candidates)
        if (File.Exists(c)) return c;
    return candidates[0];
}

/// <summary>Finds the USB device address matching the doc's known MK20 VID:PID pairs.</summary>
static int FindMk20DeviceAddress(string tsharkPath, string capturePath)
{
    (int Vid, int Pid)[] known = { (0x1d6b, 0x0104), (0x1234, 0x5678) };

    var psi = new ProcessStartInfo(tsharkPath)
    {
        ArgumentList = { "-r", capturePath, "-Y", "usb.idVendor", "-T", "fields",
            "-e", "usb.device_address", "-e", "usb.idVendor", "-e", "usb.idProduct" },
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tshark.");
    string? line;
    while ((line = proc.StandardOutput.ReadLine()) != null)
    {
        var parts = line.Split('\t');
        if (parts.Length < 3) continue;
        if (!int.TryParse(parts[0], out int addr)) continue;
        if (!TryParseHex(parts[1], out int vid) || !TryParseHex(parts[2], out int pid)) continue;
        if (known.Any(k => k.Vid == vid && k.Pid == pid)) return addr;
    }
    proc.WaitForExit();
    return -1;
}

static bool TryParseHex(string s, out int value)
{
    s = s.Trim();
    if (s.StartsWith("0x")) s = s[2..];
    return int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out value);
}

/// <summary>Real framing: pull CDC-ACM payload bytes via the usbcom dissector's in/out payload fields.</summary>
static List<UsbRow> RunTsharkUsbcom(string tsharkPath, string capturePath, int deviceAddress)
{
    var psi = new ProcessStartInfo(tsharkPath)
    {
        ArgumentList =
        {
            "-r", capturePath,
            "-Y", $"usb.device_address=={deviceAddress} && (usbcom.data.out_payload || usbcom.data.in_payload)",
            "-T", "fields",
            "-e", "frame.number",
            "-e", "usbcom.data.out_payload",
            "-e", "usbcom.data.in_payload",
            "-E", "separator=|",
        },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tshark.");
    var rows = new List<UsbRow>();
    string? line;
    while ((line = proc.StandardOutput.ReadLine()) != null)
    {
        var parts = line.Split('|');
        if (parts.Length < 3) continue;
        if (!int.TryParse(parts[0], out int frameNo)) continue;
        string outHex = parts[1].Trim();
        string inHex = parts[2].Trim();
        if (outHex.Length > 0) rows.Add(new UsbRow(frameNo, false, outHex));
        if (inHex.Length > 0) rows.Add(new UsbRow(frameNo, true, inHex));
    }
    proc.WaitForExit();
    if (proc.ExitCode != 0) Console.WriteLine($"[tshark stderr] {proc.StandardError.ReadToEnd()}");
    return rows;
}

/// <summary>Legacy path: try the generic usb.capdata field (works for some capture/dissector combos).</summary>
static List<UsbRow> RunTsharkLegacyCapdata(string tsharkPath, string capturePath, int deviceAddress)
{
    var psi = new ProcessStartInfo(tsharkPath)
    {
        ArgumentList =
        {
            "-r", capturePath,
            "-Y", $"usb.device_address=={deviceAddress} && usb.capdata",
            "-T", "fields",
            "-e", "frame.number",
            "-e", "usb.endpoint_address.direction",
            "-e", "usb.capdata",
            "-E", "separator=|",
        },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start tshark.");
    var rows = new List<UsbRow>();
    string? line;
    while ((line = proc.StandardOutput.ReadLine()) != null)
    {
        var parts = line.Split('|');
        if (parts.Length < 3) continue;
        if (!int.TryParse(parts[0], out int frameNo)) continue;
        bool directionIn = parts[1].Trim() == "1";
        string capData = parts[2].Trim();
        if (capData.Length == 0) continue;
        rows.Add(new UsbRow(frameNo, directionIn, capData));
    }
    proc.WaitForExit();
    if (proc.ExitCode != 0) Console.WriteLine($"[tshark stderr] {proc.StandardError.ReadToEnd()}");
    return rows;
}

static byte[] HexToBytes(string hex)
{
    hex = hex.Replace(":", "").Replace(" ", "");
    var bytes = new byte[hex.Length / 2];
    for (int i = 0; i < bytes.Length; i++)
        bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
    return bytes;
}

static void DecodeRealAndPrint(byte[] stream, string label)
{
    if (stream.Length == 0) { Console.WriteLine("(no data)"); return; }

    var parser = new RealFrameParser();
    parser.Feed(stream);
    int count = 0;
    foreach (var frame in parser.DrainFrames())
    {
        count++;
        PrintRealFrame(label, frame);
    }
    if (count == 0)
    {
        Console.WriteLine($"No recognizable frames decoded from {stream.Length} raw bytes.");
    }
}

static void PrintRealFrame(string label, RealFrame frame)
{
    if (frame.Cmd == uint.MaxValue)
    {
        Console.WriteLine($"[{label}] ABORT-FILE-TRANSFER control message");
        return;
    }

    string cmdName = frame.Cmd switch
    {
        CmdValue.FindDevice => "FIND_DEVICE",
        CmdValue.SendSystemDataToDevice => "SEND_SYSTEM_DATA_TO_DEVICE",
        CmdValue.SetDeviceReload => "SET_DEVICE_RELOAD",
        CmdValue.GetDeviceTheme => "GET_DEVICE_THEME",
        CmdValue.SetDeviceBacklight => "SET_DEVICE_BL",
        CmdValue.SetDeviceScanState => "SET_DEVICE_SCAN_STATE",
        CmdValue.FileStart => "FILE_START",
        CmdValue.FileEnd => "FILE_END",
        CmdValue.GetDeviceVersion => "GET_DEVICE_VERSION",
        CmdValue.SetDeviceCanvasFlip => "SET_DEVICE_CANVASFLIP",
        CmdValue.GetDeviceScreenMessage => "GET_DEVICE_SCREENMESSAGE",
        CmdValue.SetDeviceDeleteTheme => "SET_DEVICE_DELETE_THEME",
        CmdValue.SendPixmap => "SEND_PIXMAP",
        CmdValue.DeviceProactiveEscalationCmd => "DEVICE_ProactiveEscalationCMD",
        CmdValue.RequestUploadKey => "REQUEST_UPLOAD_KEY",
        CmdValue.SendJson => "SEND_JSON",
        _ => $"cmd_{frame.Cmd}",
    };

    string crcFlag = frame.CrcOk ? "" : " [CRC-MISMATCH]";
    Console.Write($"[{label}] type={frame.PacketType} cmd={frame.Cmd} ({cmdName}) len={frame.Payload.Length}{crcFlag}  ");

    if (frame.Payload.Length == 0)
    {
        Console.WriteLine("(empty payload)");
        return;
    }

    if (frame.Cmd == CmdValue.SendSystemDataToDevice)
    {
        var kvs = SystemDataCodec.Decode(frame.Payload);
        Console.WriteLine(string.Join(", ", kvs.Select(kv => $"{kv.Key}={kv.Value}")));
        return;
    }

    // Try JSON first (covers SEND_JSON and anything else that happens to carry JSON text).
    try
    {
        using var doc = JsonDocument.Parse(frame.Payload);
        Console.WriteLine(JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = false }));
        return;
    }
    catch (JsonException) { /* fall through */ }

    if (frame.Payload.Length >= 2 && frame.Payload[0] == 0xFF && frame.Payload[1] == 0xD8)
    {
        Console.WriteLine("[JPEG data]");
        return;
    }

    var strings = QtBlobBestEffortDecoder.ExtractStrings(frame.Payload);
    if (strings.Count > 0)
    {
        Console.WriteLine("qt-strings: [" + string.Join(" | ", strings) + "]");
        return;
    }

    // SET_DEVICE_RELOAD (cmd=2) was observed as a plain UTF-8 path string with no length
    // prefix at all (unlike the Qt-QDataStream-serialized payloads of other commands) -
    // e.g. "/data/theme/MK20/<theme name>/<theme name>.Theme". Try that as a last resort.
    if (IsMostlyPrintableUtf8(frame.Payload, out string utf8Text))
    {
        Console.WriteLine("utf8: " + utf8Text);
        return;
    }

    int previewLen = Math.Min(48, frame.Payload.Length);
    Console.WriteLine("hex: " + Convert.ToHexString(frame.Payload, 0, previewLen) +
                       (frame.Payload.Length > previewLen ? "..." : ""));
}

static bool IsMostlyPrintableUtf8(byte[] payload, out string text)
{
    text = "";
    try
    {
        text = Encoding.UTF8.GetString(payload).TrimEnd('\0');
        if (text.Length == 0) return false;
        int printable = text.Count(c => c >= 0x20 || c is '\n' or '\r' or '\t' || c > 0x7F);
        return printable >= text.Length * 0.9;
    }
    catch
    {
        return false;
    }
}

static void DecodeLegacyAndPrint(byte[] stream, string label)
{
    if (stream.Length == 0) { Console.WriteLine("(no data)"); return; }

    var parser = new Mk20FrameParser();
    parser.Feed(stream);
    int count = 0;
    foreach (var frame in parser.DrainFrames())
    {
        count++;
        PrintLegacyFrame(label, frame);
    }
    if (count == 0)
    {
        Console.WriteLine($"No complete/valid A1A55A5E frames decoded from {stream.Length} raw bytes.");
    }
}

static void PrintLegacyFrame(string label, Mk20Frame frame)
{
    string cmdName = frame.Cmd switch
    {
        CmdValue.ShowJpg => "SHOW_JPG",
        CmdValue.Json => "JSON",
        CmdValue.End => "END",
        _ => $"cmd_{frame.Cmd}",
    };
    Console.Write($"[{label}] id={frame.Id} cmd={frame.Cmd} ({cmdName}) len={frame.Payload.Length}  ");
    if (frame.Cmd == CmdValue.Json)
    {
        try
        {
            using var doc = JsonDocument.Parse(frame.Payload);
            Console.WriteLine(JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = false }));
            return;
        }
        catch (JsonException) { }
    }
    if (frame.Payload.Length >= 2 && frame.Payload[0] == 0xFF && frame.Payload[1] == 0xD8)
    {
        Console.WriteLine("[JPEG data]");
        return;
    }
    int previewLen = Math.Min(32, frame.Payload.Length);
    Console.WriteLine("hex: " + Convert.ToHexString(frame.Payload, 0, previewLen) +
                       (frame.Payload.Length > previewLen ? "..." : ""));
}

static int RunSelfTest()
{
    Console.WriteLine("Self-test: encoding synthetic REAL-format frames, feeding them through RealFrameParser...");

    var jsonFrame = new RealFrame(0, CmdValue.SendJson,
        Encoding.UTF8.GetBytes("{\"method\":\"getInfo\",\"parameters\":null}"), 0, true);

    var stream = new List<byte>();
    stream.AddRange(jsonFrame.Encode());
    stream.AddRange(RealFrameHeader.AbortLiteralBytes);
    stream.AddRange(jsonFrame.Encode());

    var parser = new RealFrameParser();
    parser.Feed(stream.ToArray());
    var decoded = parser.DrainFrames().ToList();

    Console.WriteLine($"Decoded {decoded.Count} item(s) (expected: json, abort-sentinel, json):");
    foreach (var f in decoded) PrintRealFrame("selftest", f);

    bool ok = decoded.Count == 3
        && decoded[0].Cmd == CmdValue.SendJson
        && decoded[1].Cmd == uint.MaxValue
        && decoded[2].Cmd == CmdValue.SendJson;
    Console.WriteLine(ok ? "SELF-TEST PASSED" : "SELF-TEST FAILED");
    return ok ? 0 : 1;
}

internal sealed record UsbRow(int FrameNumber, bool DirectionIn, string CapData);
