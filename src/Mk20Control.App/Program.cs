using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Exceptions;
using Mk20Control.Protocol.Model;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Items;

// Interactive sandbox for Mk20Control.Protocol's confirmed MK20 device API. Every operation
// exposed here maps directly to a method on Mk20DeviceClient - see that type's XML
// documentation for the confirmation level of each one. This app does not implement
// anything the library itself doesn't already provide as a clean, reusable API.

using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(options => { options.SingleLine = true; options.TimestampFormat = "HH:mm:ss "; })
    .SetMinimumLevel(LogLevel.Debug));
var appLogger = loggerFactory.CreateLogger("Mk20Sandbox");

string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string assetsDir = Path.Combine(repoRoot, "assets");
string iconsDir = Path.Combine(assetsDir, "icons");
string backgroundsDir = Path.Combine(assetsDir, "backgrounds");

if (args.Length >= 2 && args[0] == "--dump-raw-json")
{
    DumpRawJson(args[1]);
    return;
}

if (args.Length >= 1 && args[0] == "--build-test5-local")
{
    byte[]? built = BuildFiveKeyTestTheme(iconsDir, backgroundsDir);
    if (built is null) return;
    string outPath = Path.Combine(Path.GetTempPath(), "mk20-test5-theme.Theme");
    File.WriteAllBytes(outPath, built);
    Console.WriteLine($"Saved to {outPath}");
    return;
}

Console.WriteLine("=== MK20 Control Sandbox ===");
Console.WriteLine("Reference: ../../../README.md and PROTOCOL_WAVESHARE_MK20.md");
Console.WriteLine($"Assets: {assetsDir}");

Mk20DeviceClient? client = null;

while (true)
{
    Console.WriteLine();
    Console.WriteLine("1) List serial ports");
    Console.WriteLine("2) Connect to device");
    Console.WriteLine("3) Disconnect");
    Console.WriteLine("4) Ping device (identity info)");
    Console.WriteLine("5) Set backlight level");
    Console.WriteLine("6) Push sample telemetry (system data)");
    Console.WriteLine("7) Get installed themes");
    Console.WriteLine("8) Reload a theme (by device-side path)");
    Console.WriteLine("9) Listen for key/notification events (Enter to stop)");
    Console.WriteLine("10) Decode a local .Theme file and print its structure");
    Console.WriteLine("11) Build a demo .Theme file locally (uses a generated icon) and save to disk");
    Console.WriteLine("12) Delete a theme from the device (by device-side path)");
    Console.WriteLine("13) Upload a local .Theme file to the device and activate it");
    Console.WriteLine("14) Build+upload a 5-key test theme (icons 01-05 -> keys 1-5)");
    Console.WriteLine("0) Exit");
    Console.Write("> ");
    string? choice = Console.ReadLine();

    try
    {
        switch (choice)
        {
            case "1": ListPorts(); break;
            case "2": client = await ConnectAsync(loggerFactory); break;
            case "3": await DisconnectAsync(client); break;
            case "4": await PingAsync(Require(client)); break;
            case "5": await SetBacklightAsync(Require(client)); break;
            case "6": await PushTelemetryAsync(Require(client)); break;
            case "7": await GetInstalledThemesAsync(Require(client)); break;
            case "8": await ReloadThemeAsync(Require(client)); break;
            case "9": ListenForEvents(Require(client)); break;
            case "10": DecodeLocalTheme(); break;
            case "11": BuildDemoTheme(iconsDir); break;
            case "12": await DeleteThemeAsync(Require(client)); break;
            case "13": await UploadThemeFileAsync(Require(client)); break;
            case "14": await BuildAndUploadFiveKeyTestThemeAsync(Require(client), iconsDir, backgroundsDir); break;
            case "0": if (client is not null) await client.DisposeAsync(); return;
            default: Console.WriteLine("Unknown choice."); break;
        }
    }
    catch (Mk20UnconfirmedOperationException ex)
    {
        Console.WriteLine($"[not supported] {ex.Message}");
    }
    catch (Mk20TimeoutException ex)
    {
        Console.WriteLine($"[timeout] {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[error] {ex.GetType().Name}: {ex.Message}");
    }
}

static Mk20DeviceClient Require(Mk20DeviceClient? client) =>
    client ?? throw new InvalidOperationException("Connect to the device first (option 2).");

static void ListPorts()
{
    var ports = SerialPort.GetPortNames();
    Console.WriteLine(ports.Length == 0
        ? "No serial ports found."
        : "Available ports: " + string.Join(", ", ports));
    Console.WriteLine("MK20 typically enumerates as a USB CDC-ACM device (USB VID:PID 1d6b:0104 or 1234:5678).");
}

static async Task<Mk20DeviceClient> ConnectAsync(ILoggerFactory loggerFactory)
{
    Console.Write("COM port (e.g. COM5): ");
    string port = Console.ReadLine()?.Trim() ?? "";
    if (string.IsNullOrWhiteSpace(port)) throw new InvalidOperationException("No port entered.");

    var client = Mk20DeviceClient.CreateForSerialPort(port, loggerFactory: loggerFactory);
    client.NotificationReceived += (_, e) =>
        Console.WriteLine($"[event] {e.Position} pressed={e.IsPressed}" +
                           (e.ActionDescriptor is { } d && d.TryGetValue("type", out var t) ? $" action={t.AsString}" : ""));
    client.TransportError += (_, ex) => Console.WriteLine($"[transport-error] {ex.Message}");

    await client.ConnectAsync();
    Console.WriteLine($"Connected to {port}.");
    return client;
}

static async Task DisconnectAsync(Mk20DeviceClient? client)
{
    if (client is null) { Console.WriteLine("Not connected."); return; }
    await client.DisconnectAsync();
    Console.WriteLine("Disconnected.");
}

static async Task PingAsync(Mk20DeviceClient client)
{
    var identity = await client.TryPingAsync();
    if (identity is null)
    {
        Console.WriteLine("No identity announcement observed within the timeout.");
        return;
    }
    Console.WriteLine($"version={identity.Version} screen={identity.ScreenModel} {identity.ScreenWidth}x{identity.ScreenHeight} " +
                       $"volume={identity.DeviceVolume} backlight={identity.DeviceBacklight} name={identity.DeviceName}");
}

static async Task SetBacklightAsync(Mk20DeviceClient client)
{
    Console.Write("Backlight level (0-100): ");
    int level = int.Parse(Console.ReadLine() ?? "50");
    await client.SetBacklightAsync(level);
    Console.WriteLine("Sent.");
}

static async Task PushTelemetryAsync(Mk20DeviceClient client)
{
    var rnd = new Random();
    var data = new Dictionary<string, string>
    {
        ["GPU Usage"] = $"{rnd.Next(0, 100)}%",
        ["CPU Usage"] = $"{rnd.Next(0, 100)}%",
        ["CPU Temperature"] = $"{rnd.Next(30, 80)}\u2103",
    };
    await client.PushSystemDataAsync(data);
    Console.WriteLine("Pushed: " + string.Join(", ", data.Select(kv => $"{kv.Key}={kv.Value}")));
    Console.WriteLine("(only has a visible effect if the currently loaded theme has a matching system_data_name binding)");
}

static async Task GetInstalledThemesAsync(Mk20DeviceClient client)
{
    var listing = await client.GetInstalledThemesAsync();
    Console.WriteLine($"Free space: {listing.BytesAvailable}/{listing.BytesTotal} bytes");
    foreach (var theme in listing.Themes)
        Console.WriteLine($"  {theme.Path}  (crc32=0x{theme.Crc32:x8})");
}

static async Task ReloadThemeAsync(Mk20DeviceClient client)
{
    Console.Write("Device-side theme path (e.g. /data/theme/MK20/<name>/<name>.Theme): ");
    string path = Console.ReadLine() ?? "";
    await client.ReloadThemeAsync(path, TimeSpan.FromSeconds(15));
    Console.WriteLine("Reload acknowledged.");
}

static async Task DeleteThemeAsync(Mk20DeviceClient client)
{
    Console.Write("Device-side theme path to delete (e.g. /data/theme/MK20/<name>/<name>.Theme): ");
    string path = Console.ReadLine() ?? "";
    await client.DeleteThemeAsync(path);
    Console.WriteLine("Theme deleted.");
}

static async Task UploadThemeFileAsync(Mk20DeviceClient client)
{
    Console.Write("Local .Theme file path to upload: ");
    string localPath = Console.ReadLine() ?? "";
    if (!File.Exists(localPath)) { Console.WriteLine("File not found."); return; }

    Console.Write("Device-side destination path (e.g. /data/theme/MK20/<name>/<name>.Theme): ");
    string devicePath = Console.ReadLine() ?? "";

    byte[] bytes = File.ReadAllBytes(localPath);
    Console.WriteLine($"Uploading {bytes.Length} bytes to {devicePath}...");
    await client.UploadThemeFileAsync(devicePath, bytes);
    Console.WriteLine("Upload complete and theme activated.");
}

static void ListenForEvents(Mk20DeviceClient client)
{
    Console.WriteLine("Listening for key/notification events (see NotificationReceived output above). Press Enter to stop.");
    Console.ReadLine();
}

static void DecodeLocalTheme()
{
    Console.Write("Path to a .Theme file: ");
    string path = Console.ReadLine() ?? "";
    if (!File.Exists(path)) { Console.WriteLine("File not found."); return; }

    var theme = ThemeFileCodec.Decode(File.ReadAllBytes(path));
    Console.WriteLine($"Language={theme.Language} LayoutVersion={theme.LayoutVersion} Pages={theme.Pages.Count} Assets={theme.Assets.Count}");
    foreach (var page in theme.Pages)
    {
        Console.WriteLine($"  Page {page.PageName}: {page.Items.Count} items");
        foreach (var item in page.Items.OfType<KeyItem>())
        {
            string actionDesc = item.Action switch
            {
                KeyboardAction k => $"keyboard '{k.KeyLabel}' (keycode {k.Keycode})",
                OpenWebAction w => $"open web {w.Url}",
                MouseAction => "mouse action",
                PageSwitchAction p => $"page switch (mode {p.PageSwitchMode})",
                AudioVolumeAction a => $"{a.DeviceClass} volume ({a.TargetDeviceName})",
                TextInputAction t => $"type text '{t.InputText}'",
                KeyboardSwitchAction => "switch keyboard layout",
                OpenPageAction op => $"open page {op.PageName}",
                OneLevelUpAction => "navigate to parent page",
                ControlFlowAction => "control flow (macro)",
                EncoderKeyboardAction ek => $"encoder keyboard (left={ek.LeftKeyLabel} middle={ek.MiddleKeyLabel} right={ek.RightKeyLabel})",
                EncoderFunctionAction ef => $"encoder function ({ef.RawType})",
                UnknownKeyAction u => $"unrecognized action type '{u.RawType}'",
                null => "(no action)",
                _ => "(action)",
            };
            Console.WriteLine($"    key row={item.Row} col={item.Column} icon={item.IconAssetPath}: {actionDesc}");
        }
    }
}

static void DumpRawJson(string themePath)
{
    var theme = ThemeFileCodec.Decode(File.ReadAllBytes(themePath));
    Console.WriteLine($"Pages: {theme.Pages.Count}, Assets: {theme.Assets.Count}, LayoutVersion: {theme.LayoutVersion}");
    var seenTypes = new HashSet<string>();
    foreach (var page in theme.Pages)
    {
        Console.WriteLine("Canvas: " + System.Text.Json.JsonSerializer.Serialize(page.Canvas));
        foreach (var item in page.Items)
        {
            if (!seenTypes.Add(item.RawTypeCode)) continue;
            Console.WriteLine($"[type={item.RawTypeCode} / {item.GetType().Name}]: " + item.RawJson.GetRawText());
        }
    }
}

static void BuildDemoTheme(string iconsDir)
{
    var iconFiles = Directory.Exists(iconsDir) ? Directory.GetFiles(iconsDir, "*.png") : Array.Empty<string>();
    if (iconFiles.Length == 0)
    {
        Console.WriteLine($"No icons found in {iconsDir}. Run tools\\AssetGenerator first.");
        return;
    }

    string iconPath = iconFiles[0];
    byte[] iconBytes = File.ReadAllBytes(iconPath);
    const string assetPath = "/image/demo/icon1.png";

    var keyAction = new KeyboardAction
    {
        RawType = "keyboard",
        Description = "Keyboard",
        KeyLabel = "A",
        Keycode = 4, // USB HID usage 0x04 = 'A', confirmed against real captured remaps
        RawFields = new Dictionary<string, TaggedValue>(),
    };

    var keyItem = new KeyItem
    {
        RawTypeCode = "115",
        Id = "1",
        X = 0, Y = 0, Z = 1, Width = 128, Height = 128, Rotate = 0, Scale = 1, IsLocked = false,
        Row = 0,
        Column = 0,
        IconAssetPath = assetPath,
        Action = keyAction,
        RawJson = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
    };

    var page = new ThemePage
    {
        PageName = Guid.NewGuid().ToString(),
        Canvas = new ThemeCanvas { Width = 640, Height = 656, IsFlipped = false, IsRotated = false, ShowUnit = true },
        Items = new[] { (ThemeItem)keyItem },
    };

    var theme = new ThemeFile
    {
        Language = 0,
        KeyMacroValue = Array.Empty<byte>(),
        KeyMacro = null,
        CurrentPageId = page.PageName!,
        LayoutVersion = "V3.0",
        Pages = new[] { page },
        Assets = new[] { new ThemeAsset { Path = assetPath, Data = iconBytes } },
    };

    byte[] encoded = ThemeFileCodec.Encode(theme);

    // Verify round-trip correctness before claiming success.
    var reDecoded = ThemeFileCodec.Decode(encoded);
    bool roundTripOk = reDecoded.Pages.Count == 1
        && reDecoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault() is { } rtKey
        && rtKey.Row == 0 && rtKey.Column == 0
        && rtKey.Action is KeyboardAction { Keycode: 4 }
        && reDecoded.Assets.Count == 1 && reDecoded.Assets[0].Data.Length == iconBytes.Length;

    string outPath = Path.Combine(Path.GetTempPath(), "mk20-demo-theme.Theme");
    File.WriteAllBytes(outPath, encoded);

    Console.WriteLine($"Wrote {encoded.Length} bytes to {outPath}");
    Console.WriteLine($"Round-trip decode verification: {(roundTripOk ? "PASSED" : "FAILED")}");
    Console.WriteLine("Use option 13 to upload this file to a real device.");
}

static async Task BuildAndUploadFiveKeyTestThemeAsync(Mk20DeviceClient client, string iconsDir, string backgroundsDir)
{
    byte[]? encoded = BuildFiveKeyTestTheme(iconsDir, backgroundsDir);
    if (encoded is null) return;

    string localOutPath = Path.Combine(Path.GetTempPath(), "mk20-test5-theme.Theme");
    File.WriteAllBytes(localOutPath, encoded);
    Console.WriteLine($"Also saved locally to {localOutPath} (inspect with: dotnet run -- --dump-raw-json \"{localOutPath}\").");

    Console.Write("Device-side path to store as (e.g. /data/theme/MK20/test5/test5.Theme): ");
    string devicePath = Console.ReadLine() ?? "";
    if (string.IsNullOrWhiteSpace(devicePath)) { Console.WriteLine("No path entered."); return; }

    Console.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
    await client.UploadThemeFileAsync(devicePath, encoded, TimeSpan.FromSeconds(20));
    Console.WriteLine("Upload complete and theme activated. Buttons 1-5 (top row) now show icons 01-05 and type '1'-'5'.");
}

/// <summary>
/// Builds a 5-key test theme (icons 01-05 on the top row, each typing digits '1'-'5') and
/// verifies its round-trip decode locally. Returns null (after printing a message) if
/// required assets are missing or the round-trip check fails - callers must not upload a
/// theme that failed this local check.
/// </summary>
static byte[]? BuildFiveKeyTestTheme(string iconsDir, string backgroundsDir)
{
    // USB HID keyboard usage IDs for the top-row digit keys: '1'=0x1E(30) .. '5'=0x22(34).
    // Confirmed keycode semantics: keycode 4 = 'A' (see BuildDemoTheme / README), so digits
    // follow the same standard USB HID keyboard usage table starting at 0x1E for '1'.
    var items = new List<ThemeItem>();
    var assets = new List<ThemeAsset>();

    // Every real theme examined has a main-screen background item (type 100) covering the
    // 640x512 key-grid region - include one here too so the on-device renderer has the
    // full structure it expects, rather than only bare key items. Real background items
    // also carry maxWidth/maxHeight (confirmed via dumping a real theme's raw JSON).
    string backgroundFile = Path.Combine(backgroundsDir, "gradient_main_screen_640x512.png");
    if (!File.Exists(backgroundFile))
    {
        Console.WriteLine($"Missing background file: {backgroundFile}. Run tools\\AssetGenerator first.");
        return null;
    }
    const string backgroundAssetPath = "/image/test5/background.png";
    assets.Add(new ThemeAsset { Path = backgroundAssetPath, Data = File.ReadAllBytes(backgroundFile) });
    items.Add(new BackgroundItem
    {
        RawTypeCode = "100",
        Id = "1",
        X = 0, Y = 144, Z = -2, Width = 640, Height = 512, Rotate = 0, Scale = 1, IsLocked = true,
        RawSurface = "main",
        Surface = BackgroundSurface.Main,
        AssetPath = backgroundAssetPath,
        RawJson = System.Text.Json.JsonDocument.Parse("""{"maxWidth":"640","maxHeight":"512"}""").RootElement.Clone(),
    });

    for (int i = 1; i <= 5; i++)
    {
        string iconFile = Path.Combine(iconsDir, $"icon_{i:D2}.png");
        if (!File.Exists(iconFile))
        {
            Console.WriteLine($"Missing icon file: {iconFile}. Run tools\\AssetGenerator first.");
            return null;
        }

        string assetPath = $"/image/test5/icon_{i:D2}.png";
        assets.Add(new ThemeAsset { Path = assetPath, Data = File.ReadAllBytes(iconFile) });

        var action = new KeyboardAction
        {
            RawType = "keyboard",
            Description = "Keyboard",
            KeyLabel = i.ToString(),
            Keycode = 0x1D + i, // '1'=0x1E .. '5'=0x22
            RawFields = new Dictionary<string, TaggedValue>(),
        };

        // CONFIRMED (dumping a real theme's raw JSON, see PROTOCOL_WAVESHARE_MK20.md /
        // README): real key items (type 115) do NOT carry "w"/"h" fields at all - instead
        // they use "maxWidth"/"maxHeight" (the canvas cell bounds) plus
        // "scaledWidthTo"/"scaledHeightTo" (the rendered icon size), alongside
        // "opacity"/"paths"/"soundFile"/"title"/"titleParam" which are always present.
        // Leaving these out (as an earlier version of this builder did) is suspected to
        // have caused an on-device hang when the theme was activated.
        var keyRawJson = System.Text.Json.JsonDocument.Parse($$"""
            {
              "maxWidth": "640",
              "maxHeight": "656",
              "opacity": "100",
              "paths": "",
              "scaledWidthTo": "128",
              "scaledHeightTo": "128",
              "soundFile": "",
              "title": "",
              "titleParam": "{\"FontFamily\":\"Microsoft YaHei\",\"FontSize\":24,\"FontStyle\":\"\",\"FontUnderline\":false,\"ShowImage\":true,\"ShowTitle\":true,\"TitleAlignment\":\"bottom\",\"TitleColor\":\"#ffffff\"}"
            }
            """).RootElement.Clone();

        items.Add(new KeyItem
        {
            RawTypeCode = "115",
            Id = (i + 1).ToString(),
            X = (i - 1) * 128, Y = 144, Z = 1, Rotate = 0, Scale = 1, IsLocked = false,
            Row = 0,
            Column = i - 1,
            IconAssetPath = assetPath,
            Action = action,
            RawJson = keyRawJson,
        });
    }

    var page = new ThemePage
    {
        PageName = Guid.NewGuid().ToString(),
        Canvas = new ThemeCanvas { Width = 640, Height = 656, IsFlipped = false, IsRotated = false, ShowUnit = true },
        Items = items,
    };

    var theme = new ThemeFile
    {
        Language = 0,
        KeyMacroValue = Array.Empty<byte>(),
        KeyMacro = null,
        CurrentPageId = page.PageName!,
        LayoutVersion = "V3.0",
        Pages = new[] { page },
        Assets = assets,
    };

    byte[] encoded = ThemeFileCodec.Encode(theme);

    // Verify round-trip correctness locally before touching the device.
    var reDecoded = ThemeFileCodec.Decode(encoded);
    var reKeys = reDecoded.Pages[0].Items.OfType<KeyItem>().OrderBy(k => k.Column).ToList();
    bool roundTripOk = reDecoded.Pages.Count == 1
        && reDecoded.Pages[0].Items.OfType<BackgroundItem>().Any()
        && reKeys.Count == 5
        && reDecoded.Assets.Count == 6
        && reKeys.Select((k, idx) => k.Row == 0 && k.Column == idx && k.Action is KeyboardAction ka && ka.Keycode == 0x1E + idx).All(ok => ok);

    Console.WriteLine($"Built test theme: {encoded.Length} bytes, 1 background + 5 keys, round-trip: {(roundTripOk ? "PASSED" : "FAILED")}");
    if (!roundTripOk)
    {
        Console.WriteLine("Aborting - local round-trip verification failed.");
        return null;
    }

    return encoded;
}
