using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Framing;
using Mk20Control.Protocol.Model;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Transport;

// Decodes MK20 traffic out of a Wireshark/USBPcap capture of the device's USB CDC-ACM
// bulk endpoints, using the confirmed real wire framing (see DeviceFrameHeader in
// Mk20Control.Protocol.Framing).
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
// each direction's concatenated byte stream through DeviceFrameParser.

if (args.Length >= 1 && args[0] == "--selftest")
{
    return RunSelfTest();
}

if (args.Length >= 2 && args[0] == "--theme")
{
    return RunThemeDecode(args[1]);
}

if (args.Length >= 2 && args[0] == "--theme-roundtrip")
{
    return RunThemeRoundTripCheck(args[1]);
}

if (args.Length < 1)
{
    Console.WriteLine("Usage: CaptureAnalyzer <capture.pcapng> [path-to-tshark.exe] [--hex] [--device-address=N]");
    Console.WriteLine(@"Default tshark path tried: C:\Program Files\Wireshark\tshark.exe");
    Console.WriteLine("       CaptureAnalyzer --selftest        (verifies frame/variant-map/theme-file round-trip encode+decode, no capture needed)");
    Console.WriteLine("       CaptureAnalyzer --theme <file.Theme>   (decodes a .Theme file directly, no capture needed)");
    Console.WriteLine("       CaptureAnalyzer --theme-roundtrip <file.Theme>   (decode -> encode -> decode and compares, no capture needed)");
    return 1;
}

string capturePath = args[0];
if (!File.Exists(capturePath))
{
    Console.WriteLine($"File not found: {capturePath}");
    return 1;
}

bool showHex = args.Contains("--hex");
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

var rows = RunTsharkUsbcom(tsharkPath, capturePath, deviceAddress);
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
DecodeRealAndPrint(hostToDevice.ToArray(), "H>D", showHex);

Console.WriteLine();
Console.WriteLine("=== device -> host (IN) ===");
DecodeRealAndPrint(deviceToHost.ToArray(), "D>H", showHex);

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

static byte[] HexToBytes(string hex)
{
    hex = hex.Replace(":", "").Replace(" ", "");
    var bytes = new byte[hex.Length / 2];
    for (int i = 0; i < bytes.Length; i++)
        bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
    return bytes;
}

static void DecodeRealAndPrint(byte[] stream, string label, bool showHex = false)
{
    if (stream.Length == 0) { Console.WriteLine("(no data)"); return; }

    var parser = new DeviceFrameParser();
    parser.Feed(stream);
    int count = 0;
    foreach (var frame in parser.DrainFrames())
    {
        count++;
        PrintFrame(label, frame, showHex);
    }
    if (count == 0)
    {
        Console.WriteLine($"No recognizable frames decoded from {stream.Length} raw bytes.");
    }
}

static void PrintFrame(string label, DeviceFrame frame, bool showHex = false)
{
    if (frame.IsAbortTransferMessage)
    {
        Console.WriteLine($"[{label}] ABORT-FILE-TRANSFER control message");
        return;
    }

    string cmdName = Enum.IsDefined(typeof(CommandId), frame.CommandId)
        ? ((CommandId)frame.CommandId).ToString()
        : $"cmd_{frame.CommandId}";

    string crcFlag = frame.IsChecksumValid ? "" : " [CRC-MISMATCH]";
    Console.Write($"[{label}] type={frame.PacketType} cmd={frame.CommandId} ({cmdName}) len={frame.Payload.Length}{crcFlag}  ");
    if (showHex)
    {
        Console.WriteLine();
        Console.WriteLine("  wire bytes (full frame): " + Convert.ToHexString(frame.Encode()));
        Console.Write("  decoded: ");
    }

    if (frame.Payload.Length == 0)
    {
        Console.WriteLine("(empty payload)");
        return;
    }

    if (frame.CommandId == (uint)CommandId.SendSystemDataToDevice)
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

    // SEND_PIXMAP wraps a JPEG a few bytes after a variant-map key (observed key:
    // "ScreenKey") rather than starting with the JPEG magic directly - scan for it.
    if (frame.CommandId == (uint)CommandId.SendPixmap)
    {
        int jpegAt = FindJpegMagic(frame.Payload);
        if (jpegAt >= 0)
        {
            Console.WriteLine($"[SEND_PIXMAP: JPEG data starting at payload offset {jpegAt}, {frame.Payload.Length - jpegAt} bytes]");
            return;
        }
    }

    // FIND_DEVICE and GET_DEVICE_THEME replies use a simple untagged string/string map
    // (SimpleStringMapCodec), CONFIRMED against real hardware - distinct from
    // VariantMapCodec's typeId-tagged format used elsewhere. Try it first since it's the
    // stricter/more specific shape (requires the whole payload to be consumed).
    if (SimpleStringMapCodec.TryDecode(frame.Payload, out var simpleMap))
    {
        Console.WriteLine("string-map: {" + string.Join(", ", simpleMap.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\"")) + "}");
        return;
    }

    if (VariantMapCodec.TryDecodeMapArray(frame.Payload, out var maps) && maps.Count > 0)
    {
        Console.WriteLine("variant-map: " + VariantMapCodec.ToDisplayString(maps));
        return;
    }

    var strings = ExtractPrintableStrings(frame.Payload);
    if (strings.Count > 0)
    {
        Console.WriteLine("strings: [" + string.Join(" | ", strings) + "]");
        return;
    }

    // SET_DEVICE_RELOAD was observed as a plain UTF-8 path string with no length prefix at
    // all (unlike the tagged/length-prefixed payloads of other commands) - e.g.
    // "/data/theme/MK20/<theme name>/<theme name>.Theme". Try that as a last resort.
    if (IsMostlyPrintableUtf8(frame.Payload, out string utf8Text))
    {
        Console.WriteLine("utf8: " + utf8Text);
        return;
    }

    int previewLen = Math.Min(48, frame.Payload.Length);
    Console.WriteLine("hex: " + Convert.ToHexString(frame.Payload, 0, previewLen) +
                       (frame.Payload.Length > previewLen ? "..." : ""));
}

static int FindJpegMagic(byte[] payload)
{
    for (int i = 0; i < payload.Length - 3; i++)
    {
        if (payload[i] == 0xFF && payload[i + 1] == 0xD8 && payload[i + 2] == 0xFF) return i;
    }
    return -1;
}

/// <summary>
/// Best-effort heuristic string extractor for payloads that aren't a clean tagged-value map
/// array (e.g. FILE_START/FILE_END, whose exact field-name schema hasn't been confirmed).
/// This is intentionally kept local to the analyzer tool (not part of the Protocol library's
/// confirmed API surface) since it is exploratory, not confirmed protocol behavior.
/// </summary>
static List<string> ExtractPrintableStrings(byte[] payload)
{
    var found = new List<string>();
    int pos = 0;
    while (pos + 4 <= payload.Length)
    {
        uint len = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(pos, 4));
        if (len > 0 && len % 2 == 0 && len <= 1024 && pos + 4 + len <= payload.Length)
        {
            var chars = new char[len / 2];
            bool printable = true;
            for (int i = 0; i < chars.Length; i++)
            {
                int b = pos + 4 + i * 2;
                char c = (char)((payload[b] << 8) | payload[b + 1]);
                if (c != 0 && (c < 0x20 || c > 0x7E))
                {
                    if (!(c >= 0x4E00 && c <= 0x9FFF)) { printable = false; break; } // allow CJK (theme names)
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

static int RunThemeRoundTripCheck(string themePath)
{
    if (!File.Exists(themePath))
    {
        Console.WriteLine($"File not found: {themePath}");
        return 1;
    }

    byte[] original = File.ReadAllBytes(themePath);
    ThemeFile theme;
    try
    {
        theme = ThemeFileCodec.Decode(original);
    }
    catch (Exception ex) when (ex is InvalidDataException or FormatException)
    {
        Console.WriteLine($"Could not decode as a .Theme file: {ex.Message}");
        return 1;
    }

    byte[] reEncoded = ThemeFileCodec.Encode(theme);
    ThemeFile reDecoded;
    try
    {
        reDecoded = ThemeFileCodec.Decode(reEncoded);
    }
    catch (Exception ex) when (ex is InvalidDataException or FormatException)
    {
        Console.WriteLine($"FAILED: re-encoded bytes could not be decoded: {ex.Message}");
        return 1;
    }

    bool ok = true;
    void Check(bool cond, string what)
    {
        if (!cond) { ok = false; Console.WriteLine($"  MISMATCH: {what}"); }
    }

    Check(reDecoded.Language == theme.Language, "Language");
    Check(reDecoded.LayoutVersion == theme.LayoutVersion, "LayoutVersion");
    Check(reDecoded.CurrentPageId == theme.CurrentPageId, "CurrentPageId");
    Check(reDecoded.Pages.Count == theme.Pages.Count, "Pages.Count");
    Check(reDecoded.Assets.Count == theme.Assets.Count, "Assets.Count");

    for (int p = 0; p < Math.Min(theme.Pages.Count, reDecoded.Pages.Count); p++)
    {
        Check(reDecoded.Pages[p].Items.Count == theme.Pages[p].Items.Count, $"Pages[{p}].Items.Count");
        for (int i = 0; i < Math.Min(theme.Pages[p].Items.Count, reDecoded.Pages[p].Items.Count); i++)
        {
            var a = theme.Pages[p].Items[i];
            var b = reDecoded.Pages[p].Items[i];
            Check(a.GetType() == b.GetType(), $"Pages[{p}].Items[{i}] type ({a.GetType().Name} vs {b.GetType().Name})");
            Check(a.RawTypeCode == b.RawTypeCode, $"Pages[{p}].Items[{i}].RawTypeCode");
        }
    }

    for (int a = 0; a < Math.Min(theme.Assets.Count, reDecoded.Assets.Count); a++)
    {
        Check(theme.Assets[a].Path == reDecoded.Assets[a].Path, $"Assets[{a}].Path");
        Check(theme.Assets[a].Data.Length == reDecoded.Assets[a].Data.Length, $"Assets[{a}].Data.Length");
        Check(theme.Assets[a].Data.AsSpan().SequenceEqual(reDecoded.Assets[a].Data), $"Assets[{a}].Data bytes");
    }

    Console.WriteLine(ok
        ? $"ROUND-TRIP OK: {original.Length} -> {reEncoded.Length} bytes, {theme.Pages.Count} page(s), {theme.Assets.Count} asset(s)"
        : "ROUND-TRIP FAILED (see mismatches above)");
    return ok ? 0 : 1;
}

static int RunThemeDecode(string themePath)
{
    if (!File.Exists(themePath))
    {
        Console.WriteLine($"File not found: {themePath}");
        return 1;
    }

    byte[] bytes = File.ReadAllBytes(themePath);
    Console.WriteLine($"Read {bytes.Length} bytes from {themePath}");

    ThemeFile theme;
    try
    {
        theme = ThemeFileCodec.Decode(bytes);
    }
    catch (Exception ex) when (ex is InvalidDataException or FormatException)
    {
        Console.WriteLine($"Could not decode as a .Theme file: {ex.Message}");
        return 1;
    }

    Console.WriteLine($"Language={theme.Language}, LayoutVersion={theme.LayoutVersion}, CurrentPageId={theme.CurrentPageId}");
    Console.WriteLine($"Pages: {theme.Pages.Count}, Assets: {theme.Assets.Count}");
    Console.WriteLine();

    foreach (var page in theme.Pages)
    {
        Console.WriteLine($"=== page {page.PageName} ({page.Items.Count} items, canvas {page.Canvas.Width}x{page.Canvas.Height}) ===");
        foreach (var item in page.Items)
        {
            Console.WriteLine($"  [{item.RawTypeCode}] {item.GetType().Name} id={item.Id} pos=({item.X},{item.Y}) size=({item.Width}x{item.Height})");
            if (item is Mk20Control.Protocol.Theme.Items.UnknownThemeItem)
                Console.WriteLine("      UNRECOGNIZED item type - rawJson: " + item.RawJson.GetRawText());
            if (item is Mk20Control.Protocol.Theme.Items.KeyItem key)
            {
                Console.WriteLine($"      row={key.Row} col={key.Column} icon={key.IconAssetPath}");
                if (key.Action is { } action)
                {
                    Console.WriteLine($"      action: {action.GetType().Name} ({action.RawType}) - {DescribeAction(action)}");
                    if (action is Mk20Control.Protocol.Theme.Actions.UnknownKeyAction)
                        Console.WriteLine("        UNRECOGNIZED action type - rawFields: " + string.Join(", ", action.RawFields.Select(kv => $"{kv.Key}={VariantMapCodec.ToDisplayString(kv.Value)}")));
                }
            }
        }
        Console.WriteLine();
    }

    Console.WriteLine($"=== assets ({theme.Assets.Count}) ===");
    foreach (var asset in theme.Assets)
    {
        Console.WriteLine($"  {asset.Path}  ({asset.Data.Length} bytes, {asset.Kind})");
    }

    return 0;
}

static string DescribeAction(Mk20Control.Protocol.Theme.Actions.KeyAction action) => action switch
{
    Mk20Control.Protocol.Theme.Actions.KeyboardAction k => $"keycode={k.Keycode} label='{k.KeyLabel}'",
    Mk20Control.Protocol.Theme.Actions.OpenWebAction w => $"url={w.Url}",
    Mk20Control.Protocol.Theme.Actions.MouseAction m => $"key={m.MouseKey} event={m.MouseEvent} x={m.MouseX} y={m.MouseY}",
    Mk20Control.Protocol.Theme.Actions.PageSwitchAction p => $"mode={p.PageSwitchMode} jumpTo={p.JumpToPage}",
    Mk20Control.Protocol.Theme.Actions.AudioVolumeAction a => $"device={a.DeviceClass} target='{a.TargetDeviceName}'",
    Mk20Control.Protocol.Theme.Actions.TextInputAction t => $"text='{t.InputText}'",
    Mk20Control.Protocol.Theme.Actions.KeyboardSwitchAction => "(switch keyboard layout)",
    Mk20Control.Protocol.Theme.Actions.OpenPageAction op => $"pageName={op.PageName}",
    Mk20Control.Protocol.Theme.Actions.OneLevelUpAction ol => $"pageName={ol.PageName}",
    Mk20Control.Protocol.Theme.Actions.ControlFlowAction cf => $"controlDataList={(cf.ControlDataList is null ? "(none)" : Convert.ToHexString(cf.ControlDataList))}",
    Mk20Control.Protocol.Theme.Actions.EncoderKeyboardAction ek => $"left={ek.LeftKeycode}('{ek.LeftKeyLabel}') middle={ek.MiddleKeycode}('{ek.MiddleKeyLabel}') right={ek.RightKeycode}('{ek.RightKeyLabel}')",
    Mk20Control.Protocol.Theme.Actions.EncoderFunctionAction ef => $"category={ef.Category} relatedTheme='{ef.RelatedThemePath}'",
    _ => "(unrecognized)",
};

static int RunSelfTest()
{
    bool allPassed = true;

    allPassed &= RunNamedTest("DeviceFrame encode/decode round-trip", TestFrameRoundTrip);
    allPassed &= RunNamedTest("DeviceFrameParser resync-past-corruption", TestParserResync);
    allPassed &= RunNamedTest("VariantMapCodec encode/decode round-trip", TestVariantMapRoundTrip);
    allPassed &= RunNamedTest("SystemDataCodec encode/decode round-trip", TestSystemDataRoundTrip);
    allPassed &= RunNamedTest("SimpleStringMapCodec decode of real FIND_DEVICE bytes", TestSimpleStringMapRealFindDevice);
    allPassed &= RunNamedTest("SimpleStringMapCodec encode/decode round-trip", TestSimpleStringMapRoundTrip);
    allPassed &= RunNamedTest("SimpleStringMapCodec decode of real SET_DEVICE_DELETE_THEME bytes", TestSimpleStringMapRealDeleteTheme);
    allPassed &= RunNamedTest("Mk20DeviceClient.UploadThemeFileAsync chunking matches confirmed capture", TestUploadThemeFileChunking);

    Console.WriteLine();
    Console.WriteLine(allPassed ? "ALL SELF-TESTS PASSED" : "SOME SELF-TESTS FAILED");
    return allPassed ? 0 : 1;
}

static bool RunNamedTest(string name, Func<bool> test)
{
    Console.Write($"[selftest] {name} ... ");
    bool ok;
    try { ok = test(); }
    catch (Exception ex)
    {
        Console.WriteLine($"EXCEPTION: {ex}");
        return false;
    }
    Console.WriteLine(ok ? "PASSED" : "FAILED");
    return ok;
}

static bool TestFrameRoundTrip()
{
    var original = DeviceFrame.CreateRequest((uint)CommandId.SendJson, Encoding.UTF8.GetBytes("{\"method\":\"getInfo\"}"));
    byte[] encoded = original.Encode();

    var parser = new DeviceFrameParser();
    parser.Feed(encoded);
    var decoded = parser.DrainFrames().ToList();

    return decoded.Count == 1
        && decoded[0].CommandId == original.CommandId
        && decoded[0].IsChecksumValid
        && decoded[0].Payload.SequenceEqual(original.Payload);
}

static bool TestParserResync()
{
    var frame1 = DeviceFrame.CreateRequest((uint)CommandId.SendJson, Encoding.UTF8.GetBytes("{\"a\":1}"));
    var frame2 = DeviceFrame.CreateRequest((uint)CommandId.SendJson, Encoding.UTF8.GetBytes("{\"b\":2}"));

    var stream = new List<byte>();
    stream.AddRange(frame1.Encode());
    stream.AddRange(DeviceFrameHeader.AbortTransferBytes);

    var corrupted = frame1.Encode();
    corrupted[35] ^= 0xFF; // flip a byte in the declared checksum field
    stream.AddRange(corrupted);
    stream.AddRange(frame2.Encode());

    var parser = new DeviceFrameParser();
    parser.Feed(stream.ToArray());
    var decoded = parser.DrainFrames().ToList();

    // Expected: frame1 (valid), abort sentinel, corrupted-checksum copy of frame1 (still
    // structurally well-formed, so the parser yields it rather than silently dropping it -
    // checksum validity is a flag for the caller to act on, not a reason to resync), frame2
    // (valid). The parser only resyncs past a magic when the LENGTH field is implausible or
    // the header prefix doesn't match, never merely because a checksum mismatches.
    return decoded.Count == 4
        && decoded[0].CommandId == (uint)CommandId.SendJson && decoded[0].IsChecksumValid
        && decoded[1].IsAbortTransferMessage
        && decoded[2].CommandId == (uint)CommandId.SendJson && !decoded[2].IsChecksumValid
        && decoded[3].CommandId == (uint)CommandId.SendJson && decoded[3].IsChecksumValid;
}

static bool TestVariantMapRoundTrip()
{
    var original = new Dictionary<string, TaggedValue>
    {
        ["type"] = TaggedValue.Of("keyState"),
        ["row"] = TaggedValue.Of(3),
        ["pressed"] = TaggedValue.Of(true),
        ["scale"] = TaggedValue.Of(0.4),
        ["nested"] = TaggedValue.Of(new Dictionary<string, TaggedValue> { ["inner"] = TaggedValue.Of("value") }),
        ["list"] = TaggedValue.Of(new List<TaggedValue> { TaggedValue.Of(1), TaggedValue.Of(2) }),
        ["maybeNull"] = TaggedValue.Null(10),
    };

    byte[] encoded = VariantMapCodec.EncodeMap(original);
    int pos = 0;
    var decoded = VariantMapCodec.DecodeMap(encoded, ref pos);

    return pos == encoded.Length
        && decoded["type"].AsString == "keyState"
        && decoded["row"].AsInt32 == 3
        && decoded["pressed"].AsBool == true
        && Math.Abs(decoded["scale"].AsDouble!.Value - 0.4) < 1e-9
        && decoded["nested"].AsMap!["inner"].AsString == "value"
        && decoded["list"].AsList!.Count == 2
        && decoded["maybeNull"].IsNull;
}

static bool TestSystemDataRoundTrip()
{
    var original = new List<KeyValuePair<string, string>>
    {
        new("GPU Usage", "0%"),
        new("CPU Usage", "21%"),
    };
    byte[] encoded = SystemDataCodec.Encode(original);
    var decoded = SystemDataCodec.Decode(encoded);
    return decoded.Count == 2 && decoded[0].Key == "GPU Usage" && decoded[0].Value == "0%"
        && decoded[1].Key == "CPU Usage" && decoded[1].Value == "21%";
}

// Real bytes captured from a live MK20's FIND_DEVICE reply (connected over its serial
// port), used to root-cause and confirm the fix for a decode bug where FIND_DEVICE/
// GET_DEVICE_THEME replies were incorrectly assumed to use VariantMapCodec's typeId-tagged
// format - they actually use a simpler untagged string/string map (SimpleStringMapCodec).
static bool TestSimpleStringMapRealFindDevice()
{
    const string realFindDeviceReplyHex =
        "000000080000000E00760065007200730069006F006E0000000A00560032002E00330032" +
        "0000002A00750070006700720061006400650054006F004C00610074006500730074004D" +
        "006500740068006F00640000000200310000001800730063007200650065006E005F0077" +
        "0069006400740068000000060036003400300000001800730063007200650065006E005F" +
        "006D006F00640065006C00000008004D004B003200300000001A00730063007200650065" +
        "006E005F0068006500690067006800740000000600360035003600000018006400650076" +
        "0069006300650056006F006C0075006D006500000002003700000014006400650076006900" +
        "630065004E0061006D00650000001200530063007200650065006E004B0065007900000010" +
        "0064006500760069006300650042006C0000000400380030";
    byte[] payload = Convert.FromHexString(realFindDeviceReplyHex);
    IReadOnlyList<KeyValuePair<string, string>> fields;
    try
    {
        fields = SimpleStringMapCodec.Decode(payload);
    }
    catch (System.IO.InvalidDataException)
    {
        return false;
    }

    var map = new Dictionary<string, string>(fields);
    return map.Count == 8
        && map.GetValueOrDefault("version") == "V2.32"
        && map.GetValueOrDefault("upgradeToLatestMethod") == "1"
        && map.GetValueOrDefault("screen_width") == "640"
        && map.GetValueOrDefault("screen_model") == "MK20"
        && map.GetValueOrDefault("screen_height") == "656"
        && map.GetValueOrDefault("deviceVolume") == "7"
        && map.GetValueOrDefault("deviceName") == "ScreenKey"
        && map.GetValueOrDefault("deviceBl") == "80";
}

static bool TestSimpleStringMapRoundTrip()
{
    var original = new List<KeyValuePair<string, string>>
    {
        new("bytesTotal", "2648"),
        new("bytesAvailable", "2483"),
        new("/data/theme/MK20/字母/字母.Theme", "2626723596"),
    };
    byte[] encoded = SimpleStringMapCodec.Encode(original);
    var decoded = SimpleStringMapCodec.Decode(encoded);
    return decoded.Count == 3
        && decoded[0].Key == "bytesTotal" && decoded[0].Value == "2648"
        && decoded[1].Key == "bytesAvailable" && decoded[1].Value == "2483"
        && decoded[2].Key == "/data/theme/MK20/字母/字母.Theme" && decoded[2].Value == "2626723596";
}

// Real bytes captured deleting a theme from a live MK20 (capture13.pcapng: "removed a
// theme from the device"). Confirms SET_DEVICE_DELETE_THEME (cmd=11) uses
// SimpleStringMapCodec for both the request ({path: ""}) and the reply ({"res":"1"}).
static bool TestSimpleStringMapRealDeleteTheme()
{
    byte[] requestPayload = Convert.FromHexString(
        "0000000100000038002F0064006100740061002F007400680065006D0065002F004D004B00" +
        "320030002F5B576BCD002F5B576BCD002E005400680065006D006500000000");
    byte[] replyPayload = Convert.FromHexString(
        "0000000100000006007200650073000000020031");

    var requestFields = new Dictionary<string, string>(SimpleStringMapCodec.Decode(requestPayload));
    var replyFields = new Dictionary<string, string>(SimpleStringMapCodec.Decode(replyPayload));

    return requestFields.Count == 1
        && requestFields.GetValueOrDefault("/data/theme/MK20/字母/字母.Theme") == ""
        && replyFields.Count == 1
        && replyFields.GetValueOrDefault("res") == "1";
}

// Confirmed via capture14.pcapng (a real theme install, reconstructed byte-for-byte and
// CRC-verified against the original 743,649-byte 可爱按键.Theme file): the bulk file data is
// written as fixed 4096-byte chunks with a shorter final remainder chunk, no per-chunk
// framing. This test drives Mk20DeviceClient.UploadThemeFileAsync against a fake transport
// and checks its actual chunk sizes match that confirmed real-world pattern exactly.
static bool TestUploadThemeFileChunking()
{
    const int fileLength = 743_649; // exact real confirmed file size
    const int expectedChunkSize = 4096;
    int expectedFullChunks = fileLength / expectedChunkSize; // 181
    int expectedRemainder = fileLength % expectedChunkSize;  // 2273

    var fakeBytes = new byte[fileLength];
    new Random(42).NextBytes(fakeBytes);

    var transport = new ChunkCapturingTransport();
    var client = new Mk20Control.Protocol.Client.Mk20DeviceClient(transport);
    transport.OpenAsync().GetAwaiter().GetResult();

    try
    {
        client.UploadThemeFileAsync("/data/theme/MK20/test/test.Theme", fakeBytes, TimeSpan.FromSeconds(2))
            .GetAwaiter().GetResult();
    }
    catch (Exception)
    {
        // The fake transport doesn't send a FILE_START ack payload before the bulk write
        // (matching the real capture's timing), but FILE_END's ack is synthesized
        // immediately - any exception here means the chunking itself already ran, so fall
        // through to check what was actually written.
    }

    var chunks = transport.FileBulkChunkSizes;
    if (chunks.Count != expectedFullChunks + 1) return false;
    for (int i = 0; i < expectedFullChunks; i++)
        if (chunks[i] != expectedChunkSize) return false;
    return chunks[^1] == expectedRemainder
        && transport.TotalBulkBytesWritten == fileLength;
}

/// <summary>Minimal fake transport for <see cref="TestUploadThemeFileChunking"/>: auto-acks FILE_START/FILE_END and records the exact byte-count of every WriteAsync call made after FILE_START, to verify chunk sizes.</summary>
internal sealed class ChunkCapturingTransport : Mk20Control.Protocol.Transport.ISerialTransport
{
    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
#pragma warning disable CS0067 // required by ISerialTransport but not exercised in this fake
    public event EventHandler<Exception>? ErrorOccurred;
#pragma warning restore CS0067
    public bool IsOpen { get; private set; }
    public List<int> FileBulkChunkSizes { get; } = new();
    public long TotalBulkBytesWritten { get; private set; }
    private bool _sawFileStart;

    public Task OpenAsync(CancellationToken cancellationToken = default) { IsOpen = true; return Task.CompletedTask; }
    public Task CloseAsync(CancellationToken cancellationToken = default) { IsOpen = false; return Task.CompletedTask; }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        byte[] bytes = data.ToArray();
        if (bytes.Length >= Mk20Control.Protocol.Framing.DeviceFrameHeader.HeaderLength &&
            System.Text.Encoding.ASCII.GetString(bytes, 0, Mk20Control.Protocol.Framing.DeviceFrameHeader.CommandHeaderBytes.Length) ==
                Mk20Control.Protocol.Framing.DeviceFrameHeader.CommandHeaderText)
        {
            uint commandId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(26, 4));
            if (commandId == (uint)CommandId.FileStart)
            {
                _sawFileStart = true;
                RaiseAck(CommandId.FileStart, Array.Empty<byte>());
            }
            else if (commandId == (uint)CommandId.FileEnd)
            {
                var replyPayload = SimpleStringMapCodec.Encode(new[] { new KeyValuePair<string, string>("res", "1") });
                RaiseAck(CommandId.FileEnd, replyPayload);
            }
        }
        else if (_sawFileStart)
        {
            FileBulkChunkSizes.Add(bytes.Length);
            TotalBulkBytesWritten += bytes.Length;
        }
        return Task.CompletedTask;
    }

    private void RaiseAck(CommandId commandId, byte[] payload)
    {
        var frame = new Mk20Control.Protocol.Framing.DeviceFrame(2, (uint)commandId, payload, Mk20Control.Protocol.Checksums.Crc32.Compute(payload), true);
        DataReceived?.Invoke(this, frame.Encode());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed record UsbRow(int FrameNumber, bool DirectionIn, string CapData);

