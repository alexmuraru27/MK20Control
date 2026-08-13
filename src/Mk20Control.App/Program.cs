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
using Mk20Control.Protocol.Theme.Building;
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

if (args.Length >= 1 && args[0] == "--build-fullgrid-local")
{
    byte[]? built = BuildFullGridTheme(iconsDir, backgroundsDir);
    if (built is null) return;
    string outPath = Path.Combine(Path.GetTempPath(), "mk20-fullgrid-theme.Theme");
    File.WriteAllBytes(outPath, built);
    Console.WriteLine($"Saved to {outPath}");
    return;
}

if (args.Length >= 1 && args[0] == "--build-7key-scratch")
{
    byte[]? built = BuildSevenKeyThemeFromScratch(iconsDir);
    if (built is null) return;
    string outPath = Path.Combine(Path.GetTempPath(), "mk20-7key-scratch-theme.Theme");
    File.WriteAllBytes(outPath, built);
    Console.WriteLine($"Saved to {outPath}");
    return;
}

if (args.Length >= 1 && args[0] == "--build-6page-scratch")
{
    byte[]? built = BuildSixPageThemeFromScratch(iconsDir);
    if (built is null) return;
    string outPath = Path.Combine(Path.GetTempPath(), "mk20-6page-scratch-theme.Theme");
    File.WriteAllBytes(outPath, built);
    Console.WriteLine($"Saved to {outPath}");
    return;
}

if (args.Length >= 7 && args[0] == "--add-key-local")
{
    // args: --add-key-local <themePath> <row> <col> <iconFileName> <keycode> <keyLabel>
    string themePath = args[1];
    int row = int.Parse(args[2]);
    int col = int.Parse(args[3]);
    string iconFileName = args[4];
    int keycode = int.Parse(args[5]);
    string keyLabel = args[6];
    string iconFile = Path.Combine(iconsDir, iconFileName);
    byte[]? built = AddKeyToTheme(themePath, row, col, iconFile, iconFileName, keycode, keyLabel);
    if (built is null) return;
    string outPath = Path.Combine(Path.GetTempPath(), "mk20-edited-theme.Theme");
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
    Console.WriteLine("15) Build+upload a full 20-key grid theme (icons 01-20, background) using ThemeBuilder API");
    Console.WriteLine("16) Add a key to an existing local .Theme file (via ThemeEditor) and upload it");
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
            case "15": await BuildAndUploadFullGridThemeAsync(Require(client), iconsDir, backgroundsDir); break;
            case "16": await AddKeyToThemeAndUploadAsync(Require(client), iconsDir); break;
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

    // Wrap the real serial transport with a wire-level logger - a live-USB-capture
    // substitute that records byte-for-byte exactly what this process writes/reads, so a
    // real hardware test session can be directly compared against confirmed real captures
    // (tools/Captures/*.pcapng) using the same message-sequence analysis approach.
    string wireLogPath = Path.Combine(Path.GetTempPath(), $"mk20-wirelog-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
    var innerTransport = new Mk20Control.Protocol.Transport.SerialPortTransport(port, logger: loggerFactory.CreateLogger<Mk20Control.Protocol.Transport.SerialPortTransport>());
    var loggingTransport = new Mk20Control.Protocol.Transport.WireLoggingTransport(innerTransport, wireLogPath);
    var client = new Mk20DeviceClient(loggingTransport, logger: loggerFactory.CreateLogger<Mk20DeviceClient>());
    Console.WriteLine($"Wire-level log for this session: {wireLogPath}");
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
    await client.ReloadThemeAsync(path, TimeSpan.FromSeconds(20));
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
    await client.UploadThemeFileAsync(devicePath, bytes, TimeSpan.FromSeconds(30));
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
    Console.WriteLine($"Language={theme.Language} LayoutVersion={theme.LayoutVersion} Pages={theme.Pages.Count} Assets={theme.Assets.Count} CurrentPageId={theme.CurrentPageId}");
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

static byte[]? BuildSevenKeyThemeFromScratch(string iconsDir)
{
    // Matches the layout of the user's real 5-button theme (customTheme5buttons.Theme) plus
    // a 6th key showing an animated GIF (a real, pressable KeyItem using the confirmed
    // animated-icon mechanism - paths/frameDelays - NOT a type-114 DynamicImageItem, which
    // is a separate non-interactive decoration with no key behavior) and a 7th plain
    // keyboard key - built entirely from scratch via ThemeBuilder (no editing of an existing
    // file), using the confirmed real USB HID digit keycodes consistently (keycode
    // N-1+0x1E => label matches the actual keypress; e.g. keycode 0x1E=30 really is '1',
    // keycode 0x26=38 really is '9' - confirmed against the real theme's own key #1:
    // keycode 30 with keyString "1").
    (int iconNum, int keycode, string label)[] keyboardKeys =
    {
        (16, 0x1E, "1"), // '1'=30
        (32, 0x1F, "2"), // '2'=31
        (28, 0x20, "3"), // '3'=32
        (40, 0x21, "4"), // '4'=33
        (8,  0x22, "5"), // '5'=34
        (20, 0x26, "9"), // '9'=38 - the label actually matches this keycode
        (21, 0x27, "0"), // '0'=39 - the 7th key
    };

    string desktopGif = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "pop-cat.gif");
    bool hasGif = File.Exists(desktopGif);
    if (!hasGif)
        Console.WriteLine($"Note: pop-cat.gif not found at {desktopGif} - the animated key will fall back to a static icon.");

    foreach (var (iconNum, _, _) in keyboardKeys)
    {
        string iconFile = Path.Combine(iconsDir, $"icon_{iconNum:D2}.png");
        if (!File.Exists(iconFile))
        {
            Console.WriteLine($"Missing icon file: {iconFile}.");
            return null;
        }
    }

    var builder = new ThemeBuilder();
    builder.AddPage(page =>
    {
        page.SetCanvas(640, 656);
        for (int i = 0; i < keyboardKeys.Length; i++)
        {
            var (iconNum, keycode, label) = keyboardKeys[i];
            int row = i < 5 ? 0 : 1;
            int col = i < 5 ? i : i - 5;
            string iconFile = Path.Combine(iconsDir, $"icon_{iconNum:D2}.png");

            // The 6th key (index 5, row=1/col=1) is the animated cat key - a real,
            // pressable KeyItem whose icon is the multi-frame animation, still assigned a
            // keyboard action like every other key.
            bool isAnimatedKey = i == 5 && hasGif;
            page.AddKey(row, col, key =>
            {
                if (isAnimatedKey)
                    key.AnimatedIcon("pop-cat", File.ReadAllBytes(desktopGif));
                else
                    key.Icon($"icon_{iconNum:D2}.png", File.ReadAllBytes(iconFile));
                key.Action(KeyActions.Keyboard(keycode, label));
            });
        }
    });

    var theme = builder.Build();
    byte[] encoded = ThemeFileCodec.Encode(theme);

    var reDecoded = ThemeFileCodec.Decode(encoded);
    var reKeys = reDecoded.Pages[0].Items.OfType<KeyItem>().ToList();
    bool roundTripOk = reDecoded.Pages.Count == 1
        && reKeys.Count == keyboardKeys.Length
        && reKeys.All(k => k.Action is KeyboardAction)
        && reDecoded.Pages[0].Encoder is not null;

    Console.WriteLine($"Built 7-key-from-scratch theme: {encoded.Length} bytes, {reKeys.Count} pressable keyboard key(s)" +
        $"{(hasGif ? " (1 of which shows an animated GIF)" : "")}, {reDecoded.Assets.Count} asset(s), round-trip: {(roundTripOk ? "PASSED" : "FAILED")}");
    if (!roundTripOk)
    {
        Console.WriteLine("Aborting - local round-trip verification failed.");
        return null;
    }

    return encoded;
}

static byte[]? BuildSixPageThemeFromScratch(string iconsDir)
{
    // 6 pages, each a full 4x5 grid (20 keys - confirmed real MK20 main-screen grid size).
    // Bottom-left (row=3,col=0) = previous page, bottom-right (row=3,col=4) = next page on
    // every page (a ring: page 6's "next" goes back to page 1, page 1's "previous" goes to
    // page 6 - PageSwitchAction is always relative, not an absolute jump, so this needs no
    // special-casing). All other 18 keys per page (108 total) alternate between a numbered
    // icon and the user's animated pop-cat.gif, each assigned a sequential letter of the
    // alphabet (A-Z, wrapping after Z back to A) via KeyActions.Keyboard - demonstrating
    // KeyItemBuilder.Icon (static) and .AnimatedIcon (animated) side-by-side across many keys.
    const int rows = 4, cols = 5;
    const int pageCount = 6;

    string desktopGif = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "pop-cat.gif");
    bool hasGif = File.Exists(desktopGif);
    byte[]? gifBytes = hasGif ? File.ReadAllBytes(desktopGif) : null;
    if (!hasGif)
        Console.WriteLine($"Note: pop-cat.gif not found at {desktopGif} - all content keys will use numbered icons instead.");

    // USB HID keycodes for 'A'-'Z' are 0x04-0x1D (4-29) in sequence.
    int letterIndex = 0;
    int iconCounter = 1; // cycles through icon_01..icon_40

    var builder = new ThemeBuilder();
    for (int p = 0; p < pageCount; p++)
    {
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    bool isBottomRow = row == rows - 1;
                    if (isBottomRow && col == 0)
                    {
                        page.AddKey(row, col, key => key
                            // Confirmed via real theme files (PS.Theme, defaultTheme.Theme):
                            // page-switch keys reuse the fixed static system icon
                            // "/static/icon/dark/PageSwitch.png" directly as their own "path"
                            // - no custom asset registration needed.
                            .IconAssetPath("/static/icon/dark/PageSwitch.png")
                            .Action(KeyActions.PreviousPage()));
                        continue;
                    }
                    if (isBottomRow && col == cols - 1)
                    {
                        page.AddKey(row, col, key => key
                            .IconAssetPath("/static/icon/dark/PageSwitch.png")
                            .Action(KeyActions.NextPage()));
                        continue;
                    }

                    char letter = (char)('A' + (letterIndex % 26));
                    int keycode = 0x04 + (letterIndex % 26);
                    letterIndex++;

                    bool useGif = hasGif && (letterIndex % 2 == 0);
                    int iconNum = ((iconCounter - 1) % 40) + 1;
                    iconCounter++;

                    page.AddKey(row, col, key =>
                    {
                        if (useGif)
                            key.AnimatedIcon($"popcat_{p}_{row}_{col}", gifBytes!);
                        else
                            key.Icon($"icon_{iconNum:D2}.png", File.ReadAllBytes(Path.Combine(iconsDir, $"icon_{iconNum:D2}.png")));
                        key.Action(KeyActions.Keyboard(keycode, letter.ToString()));
                    });
                }
            }
        });
    }

    var theme = builder.Build();
    byte[] encoded = ThemeFileCodec.Encode(theme);

    var decoded = ThemeFileCodec.Decode(encoded);
    var allKeys = decoded.Pages.SelectMany(pg => pg.Items.OfType<KeyItem>()).ToList();
    int expectedKeysPerPage = rows * cols;
    bool roundTripOk = decoded.Pages.Count == pageCount
        && decoded.Pages.All(pg => pg.Items.OfType<KeyItem>().Count() == expectedKeysPerPage)
        && decoded.Pages.All(pg => pg.Encoder is not null)
        && allKeys.Count(k => k.Action is PageSwitchAction psa && psa.PageSwitchMode == 1) == pageCount
        && allKeys.Count(k => k.Action is PageSwitchAction psa && psa.PageSwitchMode == 2) == pageCount
        && allKeys.Count(k => k.Action is KeyboardAction) == pageCount * (expectedKeysPerPage - 2);

    Console.WriteLine($"Built 6-page-from-scratch theme: {encoded.Length} bytes, {decoded.Pages.Count} page(s), " +
        $"{allKeys.Count} total key(s) ({expectedKeysPerPage} per page, 2 page-nav + {expectedKeysPerPage - 2} content), " +
        $"{decoded.Assets.Count} asset(s), round-trip: {(roundTripOk ? "PASSED" : "FAILED")}");
    if (!roundTripOk)
    {
        Console.WriteLine("Aborting - local round-trip verification failed.");
        return null;
    }

    return encoded;
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
        X = 0, Y = 0, Z = 1, Width = 128, Height = 128, Rotate = 0, Scale = 1, IsLocked = true,
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

        // Real key items (type 115) do NOT carry "w"/"h" fields - instead they use
        // "maxWidth"/"maxHeight" (the canvas cell bounds) plus "scaledWidthTo"/
        // "scaledHeightTo" (the rendered icon size), alongside "opacity"/"paths"/
        // "soundFile"/"title"/"titleParam", which are always present.
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
            X = (i - 1) * 128, Y = 144, Z = 1, Rotate = 0, Scale = 1, IsLocked = true,
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

/// <summary>
/// Builds a full 20-key grid theme (4 rows x 5 columns - the confirmed real MK20 main-screen
/// layout) using icons 01-20 from <paramref name="iconsDir"/> and a real background from
/// <paramref name="backgroundsDir"/>, entirely through the <c>Mk20Control.Protocol.Theme.Building</c>
/// API (no manual JSON/RawJson construction) - demonstrates the builder as the primary
/// "set a picture on a button and make it do X" entry point. Each key emits a distinct USB
/// HID keyboard keycode (digits 1-9,0 then letters A-J) so every key's effect is easy to
/// verify by typing into a text box while the theme is active. Verifies a local round-trip
/// before returning - callers must not upload a theme that failed this local check.
/// </summary>
static byte[]? BuildFullGridTheme(string iconsDir, string backgroundsDir)
{
    const int rows = 4, cols = 5; // 20 keys total, matching the confirmed real MK20 grid
    string backgroundFile = Path.Combine(backgroundsDir, "color_bars_main_screen_640x512.png");
    if (!File.Exists(backgroundFile))
    {
        Console.WriteLine($"Missing background file: {backgroundFile}. Run tools\\AssetGenerator first.");
        return null;
    }

    // USB HID keycodes: '1'-'9'=0x1E-0x26, '0'=0x27, then 'A'-'J'=0x04-0x0D.
    var keycodes = new List<(int code, string label)>();
    for (int d = 1; d <= 9; d++) keycodes.Add((0x1E + (d - 1), d.ToString()));
    keycodes.Add((0x27, "0"));
    for (int c = 0; c < 10; c++) keycodes.Add((0x04 + c, ((char)('A' + c)).ToString()));

    var builder = new ThemeBuilder();
    bool missingAsset = false;
    builder.AddPage(page =>
    {
        page.SetCanvas(640, 656);
        page.AddBackground(bg => bg.MainScreen("color_bars_main_screen_640x512.png", File.ReadAllBytes(backgroundFile)));

        int keyIndex = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int iconNum = keyIndex + 1; // icon_01..icon_20
                string iconFile = Path.Combine(iconsDir, $"icon_{iconNum:D2}.png");
                if (!File.Exists(iconFile))
                {
                    Console.WriteLine($"Missing icon file: {iconFile}. Run tools\\AssetGenerator first.");
                    missingAsset = true;
                    return;
                }
                var (code, label) = keycodes[keyIndex];
                page.AddKey(row, col, key => key
                    .Icon($"icon_{iconNum:D2}.png", File.ReadAllBytes(iconFile))
                    .Action(KeyActions.Keyboard(code, label)));
                keyIndex++;
            }
        }
    });
    if (missingAsset) return null;

    var theme = builder.Build();
    byte[] encoded = ThemeFileCodec.Encode(theme);

    // Verify round-trip correctness locally before touching the device.
    var reDecoded = ThemeFileCodec.Decode(encoded);
    var reKeys = reDecoded.Pages[0].Items.OfType<KeyItem>().ToList();
    bool roundTripOk = reDecoded.Pages.Count == 1
        && reDecoded.Pages[0].Items.OfType<BackgroundItem>().Any()
        && reKeys.Count == rows * cols
        && reDecoded.Assets.Count == rows * cols + 1;

    Console.WriteLine($"Built full-grid theme: {encoded.Length} bytes, 1 background + {rows * cols} keys, round-trip: {(roundTripOk ? "PASSED" : "FAILED")}");
    if (!roundTripOk)
    {
        Console.WriteLine("Aborting - local round-trip verification failed.");
        return null;
    }

    return encoded;
}

static async Task BuildAndUploadFullGridThemeAsync(Mk20DeviceClient client, string iconsDir, string backgroundsDir)
{
    byte[]? encoded = BuildFullGridTheme(iconsDir, backgroundsDir);
    if (encoded is null) return;

    string localOutPath = Path.Combine(Path.GetTempPath(), "mk20-fullgrid-theme.Theme");
    File.WriteAllBytes(localOutPath, encoded);
    Console.WriteLine($"Also saved locally to {localOutPath}.");

    Console.Write("Device-side path to store as (e.g. /data/theme/MK20/mygrid/mygrid.Theme): ");
    string devicePath = Console.ReadLine() ?? "";
    if (string.IsNullOrWhiteSpace(devicePath)) { Console.WriteLine("No path entered."); return; }

    Console.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
    await client.UploadThemeFileAsync(devicePath, encoded, TimeSpan.FromSeconds(30));
    Console.WriteLine("Upload complete and theme activated - 20 keys now show icons 01-20 and type digits/letters.");
}

/// <summary>
/// Loads an existing local .Theme file and adds one new key at a free grid position using
/// <see cref="ThemeEditor"/>, verifying the edit round-trips locally. Returns null (after
/// printing a message) if the position is occupied, the icon is missing, or the round-trip
/// check fails.
/// </summary>
static byte[]? AddKeyToTheme(string localPath, int row, int col, string iconFile, string iconFileName, int keycode, string keyLabel)
{
    var original = ThemeFileCodec.Decode(File.ReadAllBytes(localPath));
    var editor = new ThemeEditor(original);
    if (editor.Page(0).FindKey(row, col) is not null)
    {
        Console.WriteLine($"A key already exists at row={row}, col={col}. Aborting to avoid overwriting it silently.");
        return null;
    }

    editor.Page(0).AddKey(row, col, key => key
        .Icon(iconFileName, File.ReadAllBytes(iconFile))
        .Action(KeyActions.Keyboard(keycode, keyLabel)));

    var edited = editor.Save();
    byte[] encoded = ThemeFileCodec.Encode(edited);

    // Verify the edit round-trips locally before touching the device.
    var reDecoded = ThemeFileCodec.Decode(encoded);
    var newKey = reDecoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault(k => k.Row == row && k.Column == col);
    bool roundTripOk = newKey is not null
        && newKey.Action is KeyboardAction ka && ka.Keycode == keycode
        && reDecoded.Assets.Count == original.Assets.Count + 1;

    Console.WriteLine($"Edited theme: {encoded.Length} bytes ({original.Assets.Count} -> {reDecoded.Assets.Count} assets), round-trip: {(roundTripOk ? "PASSED" : "FAILED")}");
    if (!roundTripOk)
    {
        Console.WriteLine("Aborting - local round-trip verification failed.");
        return null;
    }

    return encoded;
}

/// <summary>
/// Interactive wrapper for <see cref="AddKeyToTheme"/>: prompts for all parameters, saves
/// the edited theme locally, then uploads it. Demonstrates the "edit an existing/real
/// theme" workflow distinct from building a brand-new one via <see cref="ThemeBuilder"/>.
/// </summary>
static async Task AddKeyToThemeAndUploadAsync(Mk20DeviceClient client, string iconsDir)
{
    Console.Write("Local .Theme file path to load (e.g. a real ScreenKeyWindows theme): ");
    string localPath = Console.ReadLine() ?? "";
    if (!File.Exists(localPath)) { Console.WriteLine("File not found."); return; }

    Console.Write("Row for the new key (e.g. 1): ");
    if (!int.TryParse(Console.ReadLine(), out int row)) { Console.WriteLine("Invalid row."); return; }
    Console.Write("Column for the new key (e.g. 1): ");
    if (!int.TryParse(Console.ReadLine(), out int col)) { Console.WriteLine("Invalid column."); return; }
    Console.Write("Icon file name under assets/icons (e.g. icon_01.png): ");
    string iconFileName = Console.ReadLine() ?? "";
    string iconFile = Path.Combine(iconsDir, iconFileName);
    if (!File.Exists(iconFile)) { Console.WriteLine($"Icon not found: {iconFile}"); return; }
    Console.Write("USB HID keycode for this key (decimal, e.g. 36 for '7'): ");
    if (!int.TryParse(Console.ReadLine(), out int keycode)) { Console.WriteLine("Invalid keycode."); return; }
    Console.Write("Key label (e.g. 7): ");
    string keyLabel = Console.ReadLine() ?? "";

    byte[]? encoded = AddKeyToTheme(localPath, row, col, iconFile, iconFileName, keycode, keyLabel);
    if (encoded is null) return;

    string localOutPath = Path.Combine(Path.GetTempPath(), "mk20-edited-theme.Theme");
    File.WriteAllBytes(localOutPath, encoded);
    Console.WriteLine($"Also saved locally to {localOutPath}.");

    Console.Write("Device-side path to store as (e.g. /data/theme/MK20/customTheme5buttons/customTheme5buttons.Theme): ");
    string devicePath = Console.ReadLine() ?? "";
    if (string.IsNullOrWhiteSpace(devicePath)) { Console.WriteLine("No path entered."); return; }

    Console.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
    await client.UploadThemeFileAsync(devicePath, encoded, TimeSpan.FromSeconds(30));
    Console.WriteLine("Upload complete and theme activated.");
}
