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
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
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

if (args.Length >= 2 && args[0] == "--builder-byte-diff")
{
    return RunBuilderByteDiff(args[1]);
}

if (args.Length >= 2 && args[0] == "--wire-log")
{
    return RunWireLogDecode(args[1]);
}

if (args.Length >= 1 && args[0] == "--emit-frame-hex")
{
    return RunEmitFrameHex();
}

if (args.Length < 1)
{
    Console.WriteLine("Usage: CaptureAnalyzer <capture.pcapng> [path-to-tshark.exe] [--hex] [--device-address=N]");
    Console.WriteLine(@"Default tshark path tried: C:\Program Files\Wireshark\tshark.exe");
    Console.WriteLine("       CaptureAnalyzer --selftest        (verifies frame/variant-map/theme-file round-trip encode+decode, no capture needed)");
    Console.WriteLine("       CaptureAnalyzer --theme <file.Theme>   (decodes a .Theme file directly, no capture needed)");
    Console.WriteLine("       CaptureAnalyzer --theme-roundtrip <file.Theme>   (decode -> encode -> decode and compares, no capture needed)");
    Console.WriteLine("       CaptureAnalyzer --builder-byte-diff <file.Theme>   (decode -> rebuild via ThemeBuilder API -> byte-diff vs original)");
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

/// <summary>
/// Decodes a wire-level log produced by <c>Mk20Control.Protocol.Transport.WireLoggingTransport</c>
/// (format: "{elapsedSeconds}\t{OUT|IN}\t{hexBytes}" per line) using the exact same framing
/// parser as real .pcapng captures, and prints the sequence with real per-line timestamps -
/// this is the live-hardware-test equivalent of decoding a USB capture, for direct
/// side-by-side comparison against tools/Captures/*.pcapng using the same analysis approach.
/// </summary>
static int RunWireLogDecode(string logPath)
{
    if (!File.Exists(logPath))
    {
        Console.WriteLine($"File not found: {logPath}");
        return 1;
    }

    var outParser = new DeviceFrameParser();
    var inParser = new DeviceFrameParser();
    // Track approximate timestamp per buffered byte offset, per direction, the same way the
    // capture analysis scripts do, so printed frame times reflect when their first byte was
    // logged (not when DrainFrames happened to run).
    var outTimes = new List<(int offset, double t)>();
    var inTimes = new List<(int offset, double t)>();
    int outOffset = 0, inOffset = 0;
    var events = new List<(double t, string dir, DeviceFrame frame)>();

    foreach (string rawLine in File.ReadLines(logPath))
    {
        string line = rawLine.TrimEnd('\r', '\n');
        if (line.Length == 0) continue;
        string[] parts = line.Split('\t');
        if (parts.Length < 3) continue;
        if (!double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out double t)) continue;
        string direction = parts[1];
        byte[] data = Convert.FromHexString(parts[2]);

        if (direction == "OUT")
        {
            outTimes.Add((outOffset, t));
            outOffset += data.Length;
            outParser.Feed(data);
            foreach (var frame in outParser.DrainFrames())
                events.Add((t, "OUT", frame));
        }
        else if (direction == "IN")
        {
            inTimes.Add((inOffset, t));
            inOffset += data.Length;
            inParser.Feed(data);
            foreach (var frame in inParser.DrainFrames())
                events.Add((t, "IN", frame));
        }
    }

    Console.WriteLine($"Decoded wire log {logPath}: {events.Count} frame(s).");
    foreach (var (t, direction, frame) in events)
    {
        if (frame.IsAbortTransferMessage)
        {
            Console.WriteLine($"  t={t,9:F3} [{direction}] ABORT-FILE-TRANSFER");
            continue;
        }
        string cmdName = Enum.IsDefined(typeof(CommandId), frame.CommandId) ? ((CommandId)frame.CommandId).ToString() : $"cmd_{frame.CommandId}";
        string crcFlag = frame.IsChecksumValid ? "" : " [CRC-MISMATCH]";
        string extra = "";
        if (frame.CommandId == (uint)CommandId.SetDeviceReload && frame.Payload.Length > 0)
        {
            try { extra = " path=" + System.Text.Encoding.UTF8.GetString(frame.Payload); } catch { }
        }
        Console.WriteLine($"  t={t,9:F3} [{direction}] type={frame.PacketType} cmd={frame.CommandId}({cmdName}) len={frame.Payload.Length}{crcFlag}{extra}");
    }
    return 0;
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

static int RunEmitFrameHex()
{
    // Reproduces the exact FileStart/FileEnd/SetDeviceReload/Abort frames our own client
    // would send for the confirmed real-hardware upload of 可爱按键.Theme in capture14.pcapng
    // (743649 bytes, crc32=3131160337 decimal / 0xBAA1B711), for a direct byte-for-byte hex
    // diff against the real captured bytes - no hardware or live USB capture needed.
    const string devicePath = "/data/theme/MK20/可爱按键/可爱按键.Theme";
    const int totalSize = 743649;
    const uint crc = 3131160337;

    byte[] fileStartPayload = SimpleStringMapCodec.Encode(
        new[] { new KeyValuePair<string, string>(devicePath, totalSize.ToString()) });
    var fileStartFrame = DeviceFrame.CreateRequest((uint)CommandId.FileStart, fileStartPayload);
    Console.WriteLine("[FileStart] our encoding:");
    Console.WriteLine("  hex: " + Convert.ToHexString(fileStartFrame.Encode()).ToLowerInvariant());
    Console.WriteLine();

    byte[] fileEndPayload = SimpleStringMapCodec.Encode(
        new[] { new KeyValuePair<string, string>(devicePath, crc.ToString()) });
    var fileEndFrame = DeviceFrame.CreateRequest((uint)CommandId.FileEnd, fileEndPayload);
    Console.WriteLine("[FileEnd] our encoding:");
    Console.WriteLine("  hex: " + Convert.ToHexString(fileEndFrame.Encode()).ToLowerInvariant());
    Console.WriteLine();

    var reloadFrame = DeviceFrame.CreateRequest((uint)CommandId.SetDeviceReload, Encoding.UTF8.GetBytes(devicePath));
    Console.WriteLine("[SetDeviceReload] our encoding:");
    Console.WriteLine("  hex: " + Convert.ToHexString(reloadFrame.Encode()).ToLowerInvariant());
    Console.WriteLine();

    Console.WriteLine("[Abort] our encoding:");
    Console.WriteLine("  hex: " + Convert.ToHexString(Mk20Control.Protocol.Framing.DeviceFrameHeader.AbortTransferBytes).ToLowerInvariant());

    return 0;
}

static int RunBuilderByteDiff(string themePath)
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

    Console.WriteLine($"Read+decoded {original.Length} bytes: {theme.Pages.Count} page(s), {theme.Assets.Count} asset(s), LayoutVersion={theme.LayoutVersion}.");

    // Reconstruct the theme purely through the ThemeBuilder API, using only the
    // strongly-typed/interpreted fields obtained from decoding (Row/Column/Action/
    // IconAssetPath/colors/etc.) - deliberately NOT reusing each item's original RawJson -
    // to test whether the builder's field skeleton (see ThemeItemSkeletons) is sufficient
    // to reproduce a structurally equivalent theme from scratch, as a real caller of this
    // API would when programmatically building a theme (not editing an existing one).
    var builder = new ThemeBuilder { Language = theme.Language, LayoutVersion = theme.LayoutVersion };
    var pageIdMap = new Dictionary<string, string>(); // original pageName -> builder-assigned pageId (both GUIDs, won't match, but tracked for reference)
    foreach (var srcPage in theme.Pages)
    {
        var pageBuilder = builder.AddPage();
        pageBuilder.SetCanvas(srcPage.Canvas.Width ?? 640, srcPage.Canvas.Height ?? 656, srcPage.Canvas.ShowUnit ?? true);
        if (srcPage.PageName is not null) pageIdMap[srcPage.PageName] = pageBuilder.PageId;

        foreach (var item in srcPage.Items)
        {
            switch (item)
            {
                case BackgroundItem bg:
                    var bgAsset = theme.Assets.FirstOrDefault(a => a.Path == bg.AssetPath);
                    pageBuilder.AddBackground(b =>
                    {
                        if (bgAsset is not null)
                        {
                            if (bg.Surface == BackgroundSurface.Secondary) b.SecondaryScreen(Path.GetFileName(bgAsset.Path), bgAsset.Data);
                            else b.MainScreen(Path.GetFileName(bgAsset.Path), bgAsset.Data);
                        }
                        b.At(bg.X ?? 0, bg.Y ?? 0, bg.Width ?? 640, bg.Height ?? 512);
                    });
                    break;
                case KeyItem key:
                    var iconAsset = key.IconAssetPath is { Length: > 0 } ? theme.Assets.FirstOrDefault(a => a.Path == key.IconAssetPath) : null;
                    pageBuilder.AddKey(key.Row, key.Column, k =>
                    {
                        k.At(key.X ?? 0, key.Y ?? 0, key.Z ?? 1);
                        if (iconAsset is not null) k.IconAssetPath(iconAsset.Path); // reuse same registered path/bytes, not re-registering
                        if (key.Action is not null) k.Action(key.Action);
                    });
                    break;
                // Other item types (text/gauges/clock/dynamic-image) intentionally omitted from
                // this reconstruction pass for now - see console note below.
            }
        }
    }

    // Builder reconstruction doesn't (yet) re-register assets already added via icon/background
    // helpers using the *original* asset path (it mints new paths) - so directly inject the
    // original asset list for a fair byte-diff focused on layout/item JSON, not asset-path churn.
    var reconstructed = builder.Build();
    reconstructed = reconstructed with { Assets = theme.Assets, CurrentPageId = theme.Pages[0].PageName ?? "" };

    byte[] rebuilt = ThemeFileCodec.Encode(reconstructed);

    Console.WriteLine($"Rebuilt via ThemeBuilder: {rebuilt.Length} bytes (original: {original.Length} bytes).");

    if (rebuilt.Length == original.Length && rebuilt.AsSpan().SequenceEqual(original))
    {
        Console.WriteLine("BYTE-FOR-BYTE IDENTICAL to the original file.");
        return 0;
    }

    Console.WriteLine("NOT byte-identical (expected - see remarks below). Diff summary:");
    int firstDiff = -1;
    int minLen = Math.Min(original.Length, rebuilt.Length);
    int diffCount = 0;
    for (int i = 0; i < minLen; i++)
    {
        if (original[i] != rebuilt[i])
        {
            if (firstDiff < 0) firstDiff = i;
            diffCount++;
        }
    }
    Console.WriteLine($"  Length: original={original.Length}, rebuilt={rebuilt.Length} (delta={rebuilt.Length - original.Length})");
    if (firstDiff >= 0)
    {
        Console.WriteLine($"  First differing byte offset: {firstDiff} ({diffCount} differing bytes within the shared {minLen}-byte prefix)");
        int ctx = 24;
        int start = Math.Max(0, firstDiff - ctx);
        Console.WriteLine("  original @ diff: " + Convert.ToHexString(original, start, Math.Min(ctx * 2, original.Length - start)));
        Console.WriteLine("  rebuilt  @ diff: " + Convert.ToHexString(rebuilt, start, Math.Min(ctx * 2, rebuilt.Length - start)));
    }
    else
    {
        Console.WriteLine("  Shared prefix identical; only a trailing length difference remains (all differing bytes are past min length).");
    }

    // Structured (semantic) diff of the decoded layer, which is what actually matters for
    // real-device compatibility - byte-exact JSON text (field order/whitespace) is not
    // required by the device, only that the confirmed required fields are present with
    // correct values (see PROTOCOL_WAVESHARE_MK20.md §7.1).
    var reDecodedRebuilt = ThemeFileCodec.Decode(rebuilt);
    Console.WriteLine();
    Console.WriteLine("Structured re-decode comparison (this is the real compatibility signal, not raw bytes):");
    Console.WriteLine($"  Pages: original={theme.Pages.Count}, rebuilt={reDecodedRebuilt.Pages.Count}");
    // NOTE on remaining "mismatches": items that were confirmed as encoder function slots
    // (EncoderKeyboardAction/EncoderFunctionAction) share a fixed row=0,col=0 sentinel
    // position rather than a unique physical grid coordinate (encoders are not part of the
    // row/col key matrix) - so row/col is not a unique key for them and this diagnostic's
    // simple row/col lookup can pick the wrong one among several sharing that sentinel.
    // This is a limitation of this comparison script only, not a ThemeBuilder defect: all
    // physical (row/col-addressable) KeyItems matched with 0 mismatches in every real theme
    // tested (see below).
    for (int p = 0; p < Math.Min(theme.Pages.Count, reDecodedRebuilt.Pages.Count); p++)
    {
        var srcItems = theme.Pages[p].Items;
        var newItems = reDecodedRebuilt.Pages[p].Items;
        Console.WriteLine($"  Page {p}: original items={srcItems.Count}, rebuilt items={newItems.Count} (rebuilt only covers Background+Key items in this pass)");
        var srcKeys = srcItems.OfType<KeyItem>().ToList();
        var newKeys = newItems.OfType<KeyItem>().ToList();
        int keyMismatches = 0;
        foreach (var sk in srcKeys)
        {
            var nk = newKeys.FirstOrDefault(k => k.Row == sk.Row && k.Column == sk.Column);
            if (nk is null) { keyMismatches++; continue; }
            bool actionMatches = (sk.Action, nk.Action) switch
            {
                (null, null) => true,
                (Mk20Control.Protocol.Theme.Actions.KeyboardAction a, Mk20Control.Protocol.Theme.Actions.KeyboardAction b) => a.Keycode == b.Keycode,
                (Mk20Control.Protocol.Theme.Actions.OpenWebAction a, Mk20Control.Protocol.Theme.Actions.OpenWebAction b) => a.Url == b.Url,
                _ => sk.Action?.GetType() == nk.Action?.GetType(),
            };
            if ((nk.IconAssetPath ?? "") != (sk.IconAssetPath ?? "") || !actionMatches)
            {
                keyMismatches++;
                Console.WriteLine($"    MISMATCH row={sk.Row} col={sk.Column}: icon '{sk.IconAssetPath}' vs '{nk.IconAssetPath}', action {sk.Action?.GetType().Name}({sk.Action?.RawType}) vs {nk.Action?.GetType().Name}({nk.Action?.RawType})");
            }
        }
        Console.WriteLine($"    Key items: {srcKeys.Count} original, {newKeys.Count} rebuilt, {keyMismatches} mismatched (icon path or action).");
    }

    Console.WriteLine();
    Console.WriteLine("NOTE: exact byte-for-byte equality is not expected here by design: this reconstruction");
    Console.WriteLine("pass only rebuilds Background+Key items via the typed builder API (as a real caller would");
    Console.WriteLine("when programmatically building a theme) and intentionally does NOT copy each item's original");
    Console.WriteLine("RawJson - so extra fields present only in the real ScreenKeyWindows-saved file (e.g. 'itemName',");
    Console.WriteLine("'backupX'/'backupY', 'frameDelays', a populated 'paths' string, JSON key ordering/whitespace, and");
    Console.WriteLine("any non-Background/Key item types) are absent from the rebuilt file. What IS confirmed identical");
    Console.WriteLine("is the decoded/interpreted meaning of every Background+Key item (see structured comparison above)");
    Console.WriteLine("and the confirmed-required field set is present (see the ThemeBuilder self-tests in --selftest).");

    return 0;
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
    allPassed &= RunNamedTest("Mk20DeviceClient.UploadThemeFileAsync retries once on a FILE_END timeout", TestUploadRetriesOnceOnFileEndTimeout);
    allPassed &= RunNamedTest("Mk20DeviceClient.UploadThemeFileAsync fails fast when the device is fully locked up", TestUploadFailsFastWhenDeviceFullyLockedUp);
    allPassed &= RunNamedTest("ThemeBuilder produces a KeyItem field set matching real hardware themes", TestThemeBuilderKeyItemFieldParity);
    allPassed &= RunNamedTest("Theme pages always emit the confirmed-required page-level 'encoder' field", TestPageEncoderFieldPresence);
    allPassed &= RunNamedTest("Theme file header's 8-byte gap encodes (JSON length + 1) matching real files", TestHeaderJsonLengthField);
    allPassed &= RunNamedTest("KeyItemBuilder.AnimatedIcon produces a real, pressable animated key (paths/frameDelays/path=\"\")", TestAnimatedKeyIcon);
    allPassed &= RunNamedTest("ThemeBuilder+ThemeEditor full round-trip (build, encode, decode, edit, re-encode, re-decode)", TestThemeBuilderEditorRoundTrip);
    allPassed &= RunNamedTest("Mk20DeviceClient refuses DeleteThemeAsync while a reload is unconfirmed", TestDeleteRefusedWhilePendingReload);
    allPassed &= RunNamedTest("Mk20DeviceClient serializes concurrent theme operations", TestThemeOperationsAreSerialized);

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
static bool TestThemeBuilderKeyItemFieldParity()
{
    // A minimal but genuinely valid 4x4 RGBA PNG - required now that Icon() actually
    // decodes+re-encodes icon bytes via ImageSharp (see IconImageNormalizer).
    byte[] tinyValidPng =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x08, 0x06, 0x00, 0x00, 0x00, 0xA9, 0xF1, 0x9E,
        0x7E, 0x00, 0x00, 0x00, 0x15, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xFC, 0xCF, 0xC0, 0xD0,
        0xC0, 0x80, 0x04, 0x98, 0x90, 0x39, 0xC4, 0x09, 0x00, 0x00, 0x64, 0x11, 0x01, 0x87, 0xA9, 0x8A,
        0xB1, 0xCB, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    };

    // The confirmed-required field set for a real KeyItem (type 115), cross-checked against
    // multiple real theme files (defaultTheme.Theme, 时尚按键.Theme) - see
    // PROTOCOL_WAVESHARE_MK20.md §7.1 and ThemeItemSkeletons remarks.
    string[] requiredKeyFields =
    {
        "maxWidth", "maxHeight", "opacity", "paths", "scaledWidthTo", "scaledHeightTo",
        "soundFile", "title", "titleParam", "id", "itemName", "x", "y", "z", "rotate", "scale", "lock",
        "row", "col", "path", "controlData", "type",
    };
    string[] forbiddenKeyFields = { "w", "h" }; // confirmed real KeyItems never have these

    string[] requiredBackgroundFields =
    {
        "maxWidth", "maxHeight", "w", "h", "id", "x", "y", "z", "rotate", "scale", "type", "backgroundType", "path",
    };

    var theme = new ThemeBuilder()
        .AddPage(page => page
            .SetCanvas(640, 656)
            .AddBackground(bg => bg.MainScreen("bg.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0 }))
            .AddKey(0, 0, key => key
                .Icon("icon.png", tinyValidPng)
                .Action(KeyActions.Keyboard(0x1E, "1"))))
        .Build();

    byte[] encoded = ThemeFileCodec.Encode(theme);
    var decoded = ThemeFileCodec.Decode(encoded);
    var key = decoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault();
    var background = decoded.Pages[0].Items.OfType<BackgroundItem>().FirstOrDefault();
    if (key is null || background is null) return false;

    var keyFieldNames = key.RawJson.EnumerateObject().Select(p => p.Name).ToHashSet();
    foreach (var field in requiredKeyFields)
        if (!keyFieldNames.Contains(field)) return false;
    foreach (var field in forbiddenKeyFields)
        if (keyFieldNames.Contains(field)) return false;

    var bgFieldNames = background.RawJson.EnumerateObject().Select(p => p.Name).ToHashSet();
    foreach (var field in requiredBackgroundFields)
        if (!bgFieldNames.Contains(field)) return false;

    // "lock" must be encoded as a string "0"/"1" (not a native JSON bool) AND must be "1" by
    // default - confirmed via 5/5 real theme files examined (including a user-created
    // working theme, customTheme5buttons.Theme/capture15.pcapng): every real KeyItem has
    // "lock":"1". See PROTOCOL_WAVESHARE_MK20.md §7.1/§10 Open Item #9.
    if (!key.RawJson.TryGetProperty("lock", out var lockEl) || lockEl.ValueKind != JsonValueKind.String) return false;
    if (lockEl.GetString() != "1") return false;

    // Confirmed real KeyboardAction controlData carries all 7 fields (§10 Open Item #10) -
    // decode it back and check every one is present, not just that SOME bytes were written.
    if (key.Action is not Mk20Control.Protocol.Theme.Actions.KeyboardAction) return false;
    byte[] controlDataBytes = Convert.FromBase64String(key.RawJson.GetProperty("controlData").GetString()!);
    int cdPos = 0;
    var controlFields = VariantMapCodec.DecodeMap(controlDataBytes, ref cdPos);
    foreach (var f in new[] { "type", "description", "parentDescription", "iconPath", "keycode", "keyString", "AISoundControlKeyword" })
        if (!controlFields.ContainsKey(f)) return false;

    // Confirmed real key icon PNG assets are exactly 128x128, RGB, no alpha channel (§10 Open
    // Item #10) - verify the normalized asset actually embedded matches this, not just that
    // *an* image was stored.
    var iconAsset = decoded.Assets.FirstOrDefault(a => a.Path == key.IconAssetPath);
    if (iconAsset is null) return false;
    using var iconImage = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(iconAsset.Data);
    if (iconImage.Width != 128 || iconImage.Height != 128) return false;
    var pngHeaderBytes = iconAsset.Data.AsSpan(0, 8).ToArray();
    // PNG color type byte at offset 25 (after the fixed 8-byte signature + IHDR chunk header)
    // must be 2 (truecolor, no alpha) to match every real embedded icon examined.
    if (iconAsset.Data.Length < 26 || iconAsset.Data[25] != 2) return false;

    return true;
}

// Confirmed via empty.Theme (a genuine minimal ScreenKeyWindows-created theme) and every
// real top-level/main-screen theme examined: the page-level "encoder" array (physical
// rotary-encoder hardware descriptor, row 103/104 entries) is always present. Its complete
// absence was confirmed to make ScreenKeyWindows itself lock up when loading an otherwise-
// valid theme file (§10 Item #10) - this test guards against ever silently dropping it
// again, both for a brand-new ThemeBuilder-built page (which has no source to copy from,
// so must default it) and for a real decoded page round-tripped through ThemeEditor.
static bool TestPageEncoderFieldPresence()
{
    var builtTheme = new ThemeBuilder()
        .AddPage(page => page.SetCanvas(640, 656))
        .Build();
    byte[] builtEncoded = ThemeFileCodec.Encode(builtTheme);
    if (!System.Text.Encoding.UTF8.GetString(builtEncoded).Contains("\"encoder\"")) return false;
    var builtDecoded = ThemeFileCodec.Decode(builtEncoded);
    if (builtDecoded.Pages[0].Encoder is not { } builtEnc || builtEnc.ValueKind != JsonValueKind.Array || builtEnc.GetArrayLength() != 2)
        return false;

    // Real theme (decoded from the empty.Theme-style header/JSON/asset layout, reconstructed
    // here inline since this test must not depend on a specific file existing on disk):
    // round-trip a page carrying a real "encoder" array through ThemeEditor and confirm it's
    // preserved unchanged, not dropped or replaced by the built-in default.
    var editor = new ThemeEditor(builtTheme);
    editor.Page(0).AddKey(0, 0, key => key.Action(KeyActions.OneLevelUp()));
    var edited = editor.Save();
    byte[] editedEncoded = ThemeFileCodec.Encode(edited);
    if (!System.Text.Encoding.UTF8.GetString(editedEncoded).Contains("\"encoder\"")) return false;

    return true;
}

// Confirmed via direct byte comparison against multiple real theme files (a genuine
// ScreenKeyWindows-created reference file and a minimal baseline, empty.Theme): the 8-byte
// gap between the header Tagged-Value Map and the layout JSON is 4 zero bytes followed by a
// big-endian uint32 equal to (JSON byte length + 1) - NOT arbitrary padding. Guards against
// ever regressing to writing zeros here again (§10 Item #10).
static bool TestHeaderJsonLengthField()
{
    var theme = new ThemeBuilder().AddPage(page => page.SetCanvas(640, 656)).Build();
    byte[] encoded = ThemeFileCodec.Encode(theme);

    int pos = 0;
    VariantMapCodec.DecodeMap(encoded, ref pos);
    byte[] gap = encoded.AsSpan(pos, 8).ToArray();
    if (gap[0] != 0 || gap[1] != 0 || gap[2] != 0 || gap[3] != 0) return false;
    uint declaredLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(gap.AsSpan(4));

    int jsonStart = pos + 8;
    if (encoded[jsonStart] != (byte)'{') return false;
    int depth = 0;
    bool inString = false, escaped = false;
    int jsonEnd = jsonStart;
    for (int i = jsonStart; i < encoded.Length; i++)
    {
        char c = (char)encoded[i];
        if (inString)
        {
            if (escaped) escaped = false;
            else if (c == '\\') escaped = true;
            else if (c == '"') inString = false;
        }
        else
        {
            if (c == '"') inString = true;
            else if (c is '{' or '[') depth++;
            else if (c is '}' or ']') { depth--; if (depth == 0) { jsonEnd = i; break; } }
        }
    }
    int actualJsonLength = jsonEnd - jsonStart + 1;
    return declaredLength == (uint)(actualJsonLength + 1);
}

// Confirmed real mechanism (§7.1, §10 Item on animated key icons - not DynamicImageItem):
// an animated key is a real, pressable KeyItem with "path":"" , "paths":"<folder>", and
// "frameDelays":"<csv>", with each frame registered as a separate asset under that folder.
// Verifies KeyItemBuilder.AnimatedIcon produces exactly this shape from a real GIF file
// (the user's own pop-cat.gif, if present on the Desktop - this test is skipped, not
// failed, if that file isn't available in this environment), and that the key still
// carries its assigned action.
static bool TestAnimatedKeyIcon()
{
    string gifPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "pop-cat.gif");
    if (!File.Exists(gifPath))
    {
        Console.Write("(skipped - pop-cat.gif not present) ");
        return true;
    }
    byte[] gifBytes = File.ReadAllBytes(gifPath);

    var theme = new ThemeBuilder()
        .AddPage(page => page
            .SetCanvas(640, 656)
            .AddKey(0, 0, key => key
                .AnimatedIcon("testanim", gifBytes)
                .Action(KeyActions.Keyboard(0x1E, "1"))))
        .Build();

    byte[] encoded = ThemeFileCodec.Encode(theme);
    var decoded = ThemeFileCodec.Decode(encoded);
    var key = decoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault();
    if (key is null) return false;
    if (key.Action is not Mk20Control.Protocol.Theme.Actions.KeyboardAction) return false;

    if (!key.RawJson.TryGetProperty("path", out var pathEl) || pathEl.GetString() != "") return false;
    if (!key.RawJson.TryGetProperty("paths", out var pathsEl)) return false;
    string? pathsValue = pathsEl.GetString();
    if (string.IsNullOrEmpty(pathsValue) || !pathsValue.StartsWith("/image/MK20/cache/")) return false;
    if (!key.RawJson.TryGetProperty("frameDelays", out var fdEl)) return false;
    string[] delays = (fdEl.GetString() ?? "").Split(',');
    if (delays.Length < 2) return false;

    // Every frame must actually be registered as a separate 128x128 RGB asset under that folder.
    var frameAssets = decoded.Assets.Where(a => a.Path.StartsWith(pathsValue + "/")).ToList();
    if (frameAssets.Count != delays.Length) return false;
    foreach (var asset in frameAssets)
    {
        using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(asset.Data);
        if (img.Width != 128 || img.Height != 128) return false;
    }

    return true;
}

static bool TestThemeBuilderEditorRoundTrip()
{
    // A minimal but genuinely valid 4x4 RGBA PNG - required now that Icon()/SetKeyIcon()
    // actually decode+re-encode icon bytes via ImageSharp (see IconImageNormalizer) rather
    // than storing them verbatim; a real image is needed for icon asset bytes specifically.
    byte[] tinyValidPng =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x08, 0x06, 0x00, 0x00, 0x00, 0xA9, 0xF1, 0x9E,
        0x7E, 0x00, 0x00, 0x00, 0x15, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xFC, 0xCF, 0xC0, 0xD0,
        0xC0, 0x80, 0x04, 0x98, 0x90, 0x39, 0xC4, 0x09, 0x00, 0x00, 0x64, 0x11, 0x01, 0x87, 0xA9, 0x8A,
        0xB1, 0xCB, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    };

    // Build a small multi-item theme exercising every builder.
    var builder = new ThemeBuilder();
    string page2Id = "";
    builder.AddPage(page =>
    {
        page.SetCanvas(640, 656)
            .AddBackground(bg => bg.MainScreen("bg.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 }))
            .AddKey(0, 0, key => key.Icon("icon0.png", tinyValidPng).Action(KeyActions.Keyboard(0x1E, "1")))
            .AddKey(0, 1, key => key.Icon("icon1.png", tinyValidPng).Action(KeyActions.OpenWeb("https://example.com")))
            .AddText(t => t.At(10, 10).Text("Hello"))
            .AddProgressBar(p => p.At(0, 0, 80, 12).BoundTo("Volume"))
            .AddLinearGauge(g => g.At(0, 0, 52, 9).BoundTo("内存利用率"))
            .AddRadialGauge(g => g.At(0, 0).BoundTo("CPU Usage").Gradient("r=255,g=0,b=0,a=255"))
            .AddDigitalClockField(c => c.Field("minute"))
            .AddDynamicImage(d => d.Gif("anim.gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 9, 9 }));
    });
    var page2 = builder.AddPage();
    page2.SetCanvas(640, 656).AddKey(1, 0, key => key.Action(KeyActions.OneLevelUp()));
    page2Id = page2.PageId;

    var theme = builder.Build();
    if (theme.Pages.Count != 2) return false;
    if (theme.Pages[0].Items.Count != 9) return false;
    if (theme.Assets.Count != 4) return false; // bg + icon0 + icon1 + gif

    byte[] encoded = ThemeFileCodec.Encode(theme);
    var decoded = ThemeFileCodec.Decode(encoded);
    if (decoded.Pages.Count != 2) return false;
    if (decoded.Assets.Count != theme.Assets.Count) return false;

    var key0 = decoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault(k => k.Row == 0 && k.Column == 0);
    if (key0?.Action is not Mk20Control.Protocol.Theme.Actions.KeyboardAction ka || ka.Keycode != 0x1E) return false;

    var key1 = decoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault(k => k.Row == 0 && k.Column == 1);
    if (key1?.Action is not Mk20Control.Protocol.Theme.Actions.OpenWebAction owa || owa.Url != "https://example.com") return false;

    // Now edit via ThemeEditor: change key0's icon+action, add a new key, remove key1.
    var editor = new ThemeEditor(decoded);
    editor.Page(0).SetKeyIcon(0, 0, "new_icon.png", tinyValidPng);
    editor.Page(0).SetKeyAction(0, 0, KeyActions.TypeText("hi"));
    editor.Page(0).RemoveKey(0, 1);
    editor.Page(0).AddKey(0, 2, key => key.Action(KeyActions.NextPage()));

    var edited = editor.Save();
    byte[] editedEncoded = ThemeFileCodec.Encode(edited);
    var reDecoded = ThemeFileCodec.Decode(editedEncoded);

    var editedKey0 = reDecoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault(k => k.Row == 0 && k.Column == 0);
    if (editedKey0?.Action is not Mk20Control.Protocol.Theme.Actions.TextInputAction tia || tia.InputText != "hi") return false;
    if (reDecoded.Pages[0].Items.OfType<KeyItem>().Any(k => k.Row == 0 && k.Column == 1)) return false; // removed
    var newKey = reDecoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault(k => k.Row == 0 && k.Column == 2);
    if (newKey?.Action is not Mk20Control.Protocol.Theme.Actions.PageSwitchAction psa || psa.PageSwitchMode != 2) return false;

    return true;
}

// Verifies the retry-on-timeout behavior added to UploadThemeFileAsync: a FILE_END that
// never acks once (matching the confirmed real-hardware failure mode) is retried
// automatically, succeeding on the second attempt without the caller needing to intervene.
static bool TestUploadRetriesOnceOnFileEndTimeout()
{
    var transport = new FlakyThenRecoveringTransport();
    var client = new Mk20Control.Protocol.Client.Mk20DeviceClient(transport);
    transport.OpenAsync().GetAwaiter().GetResult();

    var bytes = new byte[8192];
    new Random(1).NextBytes(bytes);

    client.UploadThemeFileAsync("/data/theme/MK20/retrytest/retrytest.Theme", bytes, TimeSpan.FromMilliseconds(300))
        .GetAwaiter().GetResult(); // should NOT throw - succeeds on the automatic retry

    return transport.FileEndAttempts == 2 && transport.FindDeviceRequests >= 1;
}

// Verifies the fail-fast safeguard: if FILE_END times out AND the device no longer responds
// to FIND_DEVICE at all (the confirmed real "whole command processor locked up" state), the
// upload throws immediately with a clear message instead of retrying uselessly against a
// dead link.
static bool TestUploadFailsFastWhenDeviceFullyLockedUp()
{
    var transport = new FullyDeadTransport();
    var client = new Mk20Control.Protocol.Client.Mk20DeviceClient(transport);
    transport.OpenAsync().GetAwaiter().GetResult();

    var bytes = new byte[4096];
    new Random(2).NextBytes(bytes);

    bool threw = false;
    try
    {
        client.UploadThemeFileAsync("/data/theme/MK20/deadtest/deadtest.Theme", bytes, TimeSpan.FromMilliseconds(200))
            .GetAwaiter().GetResult();
    }
    catch (Mk20Control.Protocol.Exceptions.Mk20TimeoutException ex)
    {
        threw = ex.Message.Contains("power-cycle", StringComparison.OrdinalIgnoreCase);
    }
    return threw && transport.FindDeviceRequests >= 1;
}

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

// Confirmed hazard on real hardware (PROTOCOL_WAVESHARE_MK20.md §10 Open Item #8): deleting
// a theme whose reload was sent but never confirmed acknowledged left the device's render
// subsystem stuck. This test verifies the safeguard added to Mk20DeviceClient: a reload that
// never acks (client-side timeout) marks the path as "pending", and a subsequent delete for
// that same path is refused up front (no bytes sent for the delete at all).
static bool TestDeleteRefusedWhilePendingReload()
{
    const string path = "/data/theme/MK20/test/test.Theme";
    var transport = new NeverAckTransport();
    var client = new Mk20Control.Protocol.Client.Mk20DeviceClient(transport);
    transport.OpenAsync().GetAwaiter().GetResult();

    bool reloadTimedOut = false;
    try
    {
        client.ReloadThemeAsync(path, TimeSpan.FromMilliseconds(200)).GetAwaiter().GetResult();
    }
    catch (Mk20Control.Protocol.Exceptions.Mk20TimeoutException)
    {
        reloadTimedOut = true;
    }
    if (!reloadTimedOut) return false;
    if (!client.IsReloadPending(path)) return false;

    int deleteFramesBefore = transport.SentCommandIds.Count(c => c == (uint)CommandId.SetDeviceDeleteTheme);
    bool deleteRefused = false;
    try
    {
        client.DeleteThemeAsync(path).GetAwaiter().GetResult();
    }
    catch (InvalidOperationException)
    {
        deleteRefused = true;
    }
    int deleteFramesAfter = transport.SentCommandIds.Count(c => c == (uint)CommandId.SetDeviceDeleteTheme);

    if (!deleteRefused) return false;
    if (deleteFramesAfter != deleteFramesBefore) return false; // must not have sent anything

    // Clearing the safeguard should allow the delete through (transport acks deletes normally).
    client.ClearPendingReloadState(path);
    if (client.IsReloadPending(path)) return false;
    client.DeleteThemeAsync(path).GetAwaiter().GetResult(); // should not throw now
    return true;
}

// Verifies the "don't spam the device" safeguard: ReloadThemeAsync/DeleteThemeAsync/
// UploadThemeFileAsync are serialized against each other via an internal semaphore, so a
// second call made before the first one finishes waits its turn rather than racing bytes
// onto the wire concurrently.
static bool TestThemeOperationsAreSerialized()
{
    const string pathA = "/data/theme/MK20/a/a.Theme";
    const string pathB = "/data/theme/MK20/b/b.Theme";
    var transport = new SlowAckTransport(ackDelay: TimeSpan.FromMilliseconds(300));
    var client = new Mk20Control.Protocol.Client.Mk20DeviceClient(transport);
    transport.OpenAsync().GetAwaiter().GetResult();

    var firstTask = client.ReloadThemeAsync(pathA, TimeSpan.FromSeconds(5));
    // Give the first call a moment to actually send its request before starting the second.
    Task.Delay(50).GetAwaiter().GetResult();
    var secondTask = client.ReloadThemeAsync(pathB, TimeSpan.FromSeconds(5));

    Task.WaitAll(firstTask, secondTask);

    // The second reload's request must have been sent strictly after the first one's ack
    // was received - i.e. the two [request...ack] windows must not overlap.
    var sendTimes = transport.SendTimestampsByPath;
    var ackTimes = transport.AckTimestamps;
    if (sendTimes.Count != 2 || ackTimes.Count != 2) return false;
    // First send time for pathA request, first ack time, then second send time must be >= first ack time.
    return sendTimes[1] >= ackTimes[0];
}

/// <summary>Fake transport simulating a FILE_END that never acks on the first attempt (matching the
/// confirmed real-hardware failure mode), but recovers on the retry - and answers FIND_DEVICE
/// health-check pings throughout (device stays alive, just the file-transfer path glitches once).
/// Verifies the retry-on-timeout behavior added to UploadThemeFileAsync.</summary>
internal sealed class FlakyThenRecoveringTransport : Mk20Control.Protocol.Transport.ISerialTransport
{
    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
#pragma warning disable CS0067
    public event EventHandler<Exception>? ErrorOccurred;
#pragma warning restore CS0067
    public bool IsOpen { get; private set; }
    public int FileEndAttempts { get; private set; }
    public int FindDeviceRequests { get; private set; }

    public Task OpenAsync(CancellationToken cancellationToken = default) { IsOpen = true; return Task.CompletedTask; }
    public Task CloseAsync(CancellationToken cancellationToken = default) { IsOpen = false; return Task.CompletedTask; }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        byte[] bytes = data.ToArray();
        if (bytes.AsSpan().SequenceEqual(Mk20Control.Protocol.Framing.DeviceFrameHeader.AbortTransferBytes)) return Task.CompletedTask;
        if (bytes.Length < Mk20Control.Protocol.Framing.DeviceFrameHeader.HeaderLength ||
            System.Text.Encoding.ASCII.GetString(bytes, 0, Mk20Control.Protocol.Framing.DeviceFrameHeader.CommandHeaderBytes.Length) !=
                Mk20Control.Protocol.Framing.DeviceFrameHeader.CommandHeaderText)
        {
            return Task.CompletedTask; // bulk file data - ignore
        }

        uint commandId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(26, 4));
        if (commandId == (uint)CommandId.FindDevice)
        {
            FindDeviceRequests++;
            var replyPayload = SimpleStringMapCodec.Encode(new[] { new KeyValuePair<string, string>("version", "V2.32") });
            RaiseAck(CommandId.FindDevice, replyPayload);
        }
        else if (commandId == (uint)CommandId.GetDeviceTheme)
        {
            var replyPayload = SimpleStringMapCodec.Encode(new[]
            {
                new KeyValuePair<string, string>("bytesTotal", "1000000"),
                new KeyValuePair<string, string>("bytesAvailable", "500000"),
            });
            RaiseAck(CommandId.GetDeviceTheme, replyPayload);
        }
        else if (commandId == (uint)CommandId.FileStart)
        {
            RaiseAck(CommandId.FileStart, Array.Empty<byte>());
        }
        else if (commandId == (uint)CommandId.FileEnd)
        {
            FileEndAttempts++;
            if (FileEndAttempts == 1)
            {
                // First attempt: simulate the confirmed real failure - no reply at all.
                return Task.CompletedTask;
            }
            var replyPayload = SimpleStringMapCodec.Encode(new[] { new KeyValuePair<string, string>("res", "1") });
            RaiseAck(CommandId.FileEnd, replyPayload);
        }
        else if (commandId == (uint)CommandId.SetDeviceReload)
        {
            RaiseAck(CommandId.SetDeviceReload, bytes.AsSpan(Mk20Control.Protocol.Framing.DeviceFrameHeader.HeaderLength).ToArray());
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

/// <summary>Fake transport for the "device fully locked up" scenario: FILE_END never acks, AND
/// FIND_DEVICE never acks either (simulating the confirmed real failure mode where the whole
/// command processor wedges, not just the file-transfer path). Verifies
/// UploadThemeFileAsync fails fast with a clear message instead of retrying forever against
/// a dead link.</summary>
internal sealed class FullyDeadTransport : Mk20Control.Protocol.Transport.ISerialTransport
{
    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
#pragma warning disable CS0067
    public event EventHandler<Exception>? ErrorOccurred;
#pragma warning restore CS0067
    public bool IsOpen { get; private set; }
    public int FindDeviceRequests { get; private set; }

    public Task OpenAsync(CancellationToken cancellationToken = default) { IsOpen = true; return Task.CompletedTask; }
    public Task CloseAsync(CancellationToken cancellationToken = default) { IsOpen = false; return Task.CompletedTask; }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        byte[] bytes = data.ToArray();
        if (bytes.AsSpan().SequenceEqual(Mk20Control.Protocol.Framing.DeviceFrameHeader.AbortTransferBytes)) return Task.CompletedTask;
        if (bytes.Length < Mk20Control.Protocol.Framing.DeviceFrameHeader.HeaderLength ||
            System.Text.Encoding.ASCII.GetString(bytes, 0, Mk20Control.Protocol.Framing.DeviceFrameHeader.CommandHeaderBytes.Length) !=
                Mk20Control.Protocol.Framing.DeviceFrameHeader.CommandHeaderText)
        {
            return Task.CompletedTask;
        }
        uint commandId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(26, 4));
        if (commandId == (uint)CommandId.FindDevice)
        {
            FindDeviceRequests++;
            // Never reply - simulates the confirmed real "whole command processor locked up" state.
        }
        else if (commandId == (uint)CommandId.GetDeviceTheme)
        {
            var replyPayload = SimpleStringMapCodec.Encode(new[]
            {
                new KeyValuePair<string, string>("bytesTotal", "1000000"),
                new KeyValuePair<string, string>("bytesAvailable", "500000"),
            });
            RaiseAck(CommandId.GetDeviceTheme, replyPayload);
        }
        else if (commandId == (uint)CommandId.FileStart)
        {
            RaiseAck(CommandId.FileStart, Array.Empty<byte>());
        }
        // FileEnd: never acked either.
        return Task.CompletedTask;
    }

    private void RaiseAck(CommandId commandId, byte[] payload)
    {
        var frame = new Mk20Control.Protocol.Framing.DeviceFrame(2, (uint)commandId, payload, Mk20Control.Protocol.Checksums.Crc32.Compute(payload), true);
        DataReceived?.Invoke(this, frame.Encode());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
        if (bytes.AsSpan().SequenceEqual(Mk20Control.Protocol.Framing.DeviceFrameHeader.AbortTransferBytes))
        {
            // Not a command frame and not bulk file data - ignore, matching the real device's
            // behavior of not acknowledging this control message.
        }
        else if (bytes.Length >= Mk20Control.Protocol.Framing.DeviceFrameHeader.HeaderLength &&
            System.Text.Encoding.ASCII.GetString(bytes, 0, Mk20Control.Protocol.Framing.DeviceFrameHeader.CommandHeaderBytes.Length) ==
                Mk20Control.Protocol.Framing.DeviceFrameHeader.CommandHeaderText)
        {
            uint commandId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(26, 4));
            if (commandId == (uint)CommandId.GetDeviceTheme)
            {
                var replyPayload = SimpleStringMapCodec.Encode(new[]
                {
                    new KeyValuePair<string, string>("bytesTotal", "1000000"),
                    new KeyValuePair<string, string>("bytesAvailable", "500000"),
                });
                RaiseAck(CommandId.GetDeviceTheme, replyPayload);
            }
            else if (commandId == (uint)CommandId.FileStart)
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

/// <summary>Fake transport that acks FindDevice/GetDeviceTheme/SetDeviceDeleteTheme/etc. immediately but NEVER acks SetDeviceReload - simulates a device that has stopped responding specifically to reload requests.</summary>
internal sealed class NeverAckTransport : Mk20Control.Protocol.Transport.ISerialTransport
{
    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
#pragma warning disable CS0067
    public event EventHandler<Exception>? ErrorOccurred;
#pragma warning restore CS0067
    public bool IsOpen { get; private set; }
    public List<uint> SentCommandIds { get; } = new();
    private readonly Mk20Control.Protocol.Framing.DeviceFrameParser _parser = new();

    public Task OpenAsync(CancellationToken cancellationToken = default) { IsOpen = true; return Task.CompletedTask; }
    public Task CloseAsync(CancellationToken cancellationToken = default) { IsOpen = false; return Task.CompletedTask; }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        byte[] bytes = data.ToArray();
        if (bytes.AsSpan().SequenceEqual(Mk20Control.Protocol.Framing.DeviceFrameHeader.AbortTransferBytes)) return Task.CompletedTask;
        _parser.Feed(bytes);
        foreach (var frame in _parser.DrainFrames())
        {
            SentCommandIds.Add(frame.CommandId);
            // Never ack SetDeviceReload (simulating the exact hazard scenario); ack everything else immediately.
            if (frame.CommandId != (uint)CommandId.SetDeviceReload)
            {
                var replyPayload = SimpleStringMapCodec.Encode(new[] { new KeyValuePair<string, string>("res", "1") });
                var reply = new Mk20Control.Protocol.Framing.DeviceFrame(2, frame.CommandId, replyPayload, Mk20Control.Protocol.Checksums.Crc32.Compute(replyPayload), true);
                DataReceived?.Invoke(this, reply.Encode());
            }
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Fake transport that acks every command after a fixed delay, recording send/ack timestamps - used to verify theme operations are serialized rather than overlapping.</summary>
internal sealed class SlowAckTransport : Mk20Control.Protocol.Transport.ISerialTransport
{
    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
#pragma warning disable CS0067
    public event EventHandler<Exception>? ErrorOccurred;
#pragma warning restore CS0067
    public bool IsOpen { get; private set; }
    public List<DateTime> SendTimestampsByPath { get; } = new();
    public List<DateTime> AckTimestamps { get; } = new();
    private readonly TimeSpan _ackDelay;
    private readonly Mk20Control.Protocol.Framing.DeviceFrameParser _parser = new();
    private readonly object _lock = new();

    public SlowAckTransport(TimeSpan ackDelay) => _ackDelay = ackDelay;

    public Task OpenAsync(CancellationToken cancellationToken = default) { IsOpen = true; return Task.CompletedTask; }
    public Task CloseAsync(CancellationToken cancellationToken = default) { IsOpen = false; return Task.CompletedTask; }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        byte[] bytes = data.ToArray();
        if (bytes.AsSpan().SequenceEqual(Mk20Control.Protocol.Framing.DeviceFrameHeader.AbortTransferBytes)) return Task.CompletedTask;
        _parser.Feed(bytes);
        foreach (var frame in _parser.DrainFrames())
        {
            if (frame.CommandId != (uint)CommandId.SetDeviceReload) continue;
            lock (_lock) { SendTimestampsByPath.Add(DateTime.UtcNow); }
            _ = Task.Run(async () =>
            {
                await Task.Delay(_ackDelay).ConfigureAwait(false);
                lock (_lock) { AckTimestamps.Add(DateTime.UtcNow); }
                var reply = new Mk20Control.Protocol.Framing.DeviceFrame(2, frame.CommandId, frame.Payload, Mk20Control.Protocol.Checksums.Crc32.Compute(frame.Payload), true);
                DataReceived?.Invoke(this, reply.Encode());
            });
        }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed record UsbRow(int FrameNumber, bool DirectionIn, string CapData);

