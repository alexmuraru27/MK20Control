# Mk20Control.Protocol — API Reference

**Target framework:** .NET 9
**Assembly:** `Mk20Control.Protocol` (project reference or build+reference the DLL)
**Purpose:** reusable client library for the Waveshare MK20 programmable keypad — connect
to the device, read/build/edit `.Theme` files, and drive its display and telemetry
programmatically from any .NET application (e.g. a SimHub plugin, a custom button-box
controller, or any app that wants to reflect its own state on the device's buttons).

For the underlying wire protocol and file format specification, see
`PROTOCOL_WAVESHARE_MK20.md`. This document covers only the library's public API surface
and how to use it.

---

## 1. Package layout

| Namespace | Purpose |
|---|---|
| `Mk20Control.Protocol.Client` | `Mk20DeviceClient` — connect, control, and receive events from a physical device |
| `Mk20Control.Protocol.Transport` | Serial transport abstraction (`ISerialTransport`, `SerialPortTransport`) |
| `Mk20Control.Protocol.Model` | Value types returned by the client (`DeviceIdentity`, `ThemeListing`, `KeyPosition`, `CommandId`) |
| `Mk20Control.Protocol.Codecs` | `ThemeFileCodec` — decode/encode raw `.Theme` file bytes |
| `Mk20Control.Protocol.Theme` | `ThemeFile`, `ThemePage`, `ThemeCanvas`, `ThemeAsset` — the decoded theme data model |
| `Mk20Control.Protocol.Theme.Items` | Page item types (`KeyItem`, `BackgroundItem`, `DynamicImageItem`, gauges, text, clock) |
| `Mk20Control.Protocol.Theme.Actions` | Key action types (`KeyboardAction`, `PageSwitchAction`, etc.) |
| `Mk20Control.Protocol.Theme.Building` | `ThemeBuilder`, `ThemeEditor`, `KeyActions`, `HidKey`, `KeyModifiers` — fluent theme construction/editing |
| `Mk20Control.Protocol.Exceptions` | `Mk20ProtocolException` and subtypes |

Required NuGet dependencies (already declared by the project; pull in transitively when
referencing the built DLL): `System.Text.Json`, `System.IO.Ports`,
`Microsoft.Extensions.Logging.Abstractions`, `SixLabors.ImageSharp`.

---

## 2. Connecting to the device

```csharp
using Mk20Control.Protocol.Client;

// Simplest form - opens a real COM port directly.
var client = Mk20DeviceClient.CreateForSerialPort("COM7");
await client.ConnectAsync();

// ... use client ...

await client.DisconnectAsync();
await client.DisposeAsync();
```

The MK20 enumerates as a USB CDC-ACM virtual serial port
(VID:PID `1D6B:0104` or `1234:5678`). Enumerate available ports with
`System.IO.Ports.SerialPort.GetPortNames()` if you need to auto-detect it.

### Constructor overloads

| Member | Use when |
|---|---|
| `Mk20DeviceClient.CreateForSerialPort(portName, options?, loggerFactory?)` | You just have a COM port name — the common case. |
| `new Mk20DeviceClient(ISerialTransport, options?, logger?)` | You need a custom transport (e.g. `WireLoggingTransport` for diagnostics, or a test double). |

`Mk20DeviceClientOptions` lets you override `BaudRate` (default `115200`, largely a no-op
over CDC-ACM) and `DefaultRequestTimeout` (default 5s, used by operations that don't take
their own explicit timeout).

### Logging

Pass an `ILoggerFactory` (or `ILogger<Mk20DeviceClient>`) to get structured `Debug`/`Info`/
`Warning` logs for every command sent/received, retries, and timeouts — useful when
integrating into a host application (e.g. SimHub) that has its own logging pipeline.

```csharp
var client = Mk20DeviceClient.CreateForSerialPort("COM7", loggerFactory: myLoggerFactory);
```

### Events

```csharp
client.NotificationReceived += (_, e) =>
{
    // e.Position.Row / e.Position.Column, e.IsPressed, e.ActionDescriptor (raw fields)
};
client.TransportError += (_, ex) =>
{
    // Read-loop error - connection may still be usable.
};
```

`NotificationReceived` fires for every physical key press/release **that has a bound
action in the currently loaded theme** — a key with no assigned action produces no event
at all (see the protocol spec §6.3). This is how you observe button presses to trigger
your own application logic (e.g. toggle a SimHub setting when a button is pressed).

---

## 3. Core device operations

All async methods accept an optional `TimeSpan? timeout` and `CancellationToken`.

| Method | Purpose |
|---|---|
| `TryPingAsync()` → `DeviceIdentity?` | Identity/keepalive check. Returns `null` on timeout rather than throwing (no per-request correlation exists on this protocol — see spec §4.2). |
| `SetBacklightAsync(int percentage)` | Set backlight brightness, 0–100. |
| `PushSystemDataAsync(IReadOnlyDictionary<string,string> values)` | Push live telemetry values (e.g. `{"CPU Usage": "42%"}`) that the loaded theme's gauges/text bind to by name. |
| `GetInstalledThemesAsync()` → `ThemeListing` | List installed themes (device paths + CRC-32) and free/total storage. |
| `ReloadThemeAsync(string deviceThemePath)` | Activate an already-installed theme by its device-side path. |
| `UploadThemeFileAsync(string deviceThemePath, byte[] themeFileBytes)` | Upload and activate a new/edited `.Theme` file (see §6). |
| `DeleteThemeAsync(string deviceThemePath)` | Remove an installed theme. |
| `SendJsonAsync(string json)` | Send a raw `SEND_JSON` payload — used internally for the telemetry-request contract; rarely needed directly. |

### `DeviceIdentity`

```csharp
var identity = await client.TryPingAsync();
if (identity is not null)
{
    Console.WriteLine($"{identity.DeviceName} v{identity.Version} " +
                       $"{identity.ScreenModel} {identity.ScreenWidth}x{identity.ScreenHeight}");
}
```
Strongly-typed fields: `Version`, `UpgradeToLatestMethod`, `ScreenWidth`, `ScreenModel`,
`ScreenHeight`, `DeviceVolume`, `DeviceName`, `DeviceBacklight`. `RawFields` exposes
anything not yet promoted to a typed property.

### `ThemeListing`

```csharp
var listing = await client.GetInstalledThemesAsync();
Console.WriteLine($"{listing.BytesAvailable}/{listing.BytesTotal} bytes free");
foreach (var theme in listing.Themes)
    Console.WriteLine($"{theme.Path}  crc32=0x{theme.Crc32:x8}");
```

### Pushing telemetry

```csharp
await client.PushSystemDataAsync(new Dictionary<string, string>
{
    ["CPU Usage"] = "42%",
    ["GPU Temperature"] = "61℃",
});
```
Key names are theme-defined (bound via `system_data_name` in the theme's JSON) — push
whatever keys the currently loaded theme declares via its `deviceRequestSystemData`
contract (sent automatically after every `SET_DEVICE_RELOAD`). Most integrations simply
push their own known key set on a timer.

### Operational safety (built-in)

`ReloadThemeAsync`, `DeleteThemeAsync`, and `UploadThemeFileAsync` are automatically
serialized against each other (never more than one in flight), and `DeleteThemeAsync`
refuses to delete a theme whose reload is still unconfirmed (throws
`InvalidOperationException`) — this mirrors the real vendor host's behavior and avoids a
confirmed device-firmware hang. `IsReloadPending(path)` / `ClearPendingReloadState(path)`
let you inspect or explicitly override this guard once you've independently confirmed it's
safe (e.g. after a manual power-cycle).

---

## 4. Reading a `.Theme` file

```csharp
using Mk20Control.Protocol.Codecs;

byte[] fileBytes = File.ReadAllBytes("MyTheme.Theme");
ThemeFile theme = ThemeFileCodec.Decode(fileBytes);

Console.WriteLine($"{theme.Pages.Count} page(s), {theme.Assets.Count} asset(s)");
foreach (var page in theme.Pages)
{
    foreach (var key in page.Items.OfType<KeyItem>())
        Console.WriteLine($"row={key.Row} col={key.Column} action={key.Action}");
}
```

`ThemeFileCodec.Decode(byte[])` is the sole entry point for reading — it parses the binary
header, layout JSON, and asset table into an immutable `ThemeFile` object graph. Every
field not yet promoted to a strongly-typed property remains accessible via each item's
`RawJson` (a `System.Text.Json.JsonElement`).

### Object model

```
ThemeFile
├── Language, LayoutVersion, CurrentPageId
├── KeyMacroValue, KeyMacro (opaque header bytes, preserved for round-trip fidelity)
├── Pages: IReadOnlyList<ThemePage>
│   ├── PageName (GUID string)
│   ├── Canvas: ThemeCanvas (Width, Height, IsFlipped, IsRotated, ShowUnit)
│   ├── Encoder: JsonElement? (rotary-encoder hardware descriptor, always present on a main-screen page)
│   └── Items: IReadOnlyList<ThemeItem>
│       ├── KeyItem (type 115): Row, Column, IconAssetPath, Action, RawControlDataBase64
│       ├── BackgroundItem (type 100): Surface, AssetPath (`.mp4` video only)
│       ├── DynamicImageItem (type 114): AssetPath, SystemDataName, BackgroundType ("main"/"secondary"/null)
│       ├── TextItem, ProgressBarItem, LinearGaugeItem, RadialGaugeItem, DigitalClockItem
│       └── UnknownThemeItem (any type code not yet modeled - RawJson preserved)
└── Assets: IReadOnlyList<ThemeAsset> (Path, Data, Kind)
```

### Key actions

`KeyItem.Action` is a `KeyAction?` (null if the key's `controlData` couldn't be decoded —
check `RawControlDataBase64` in that case). Concrete types in
`Mk20Control.Protocol.Theme.Actions`:

| Type | `type` string | Notable fields |
|---|---|---|
| `KeyboardAction` | `keyboard` | `Keycode` (USB HID usage; upper byte = modifier bitmask for combos), `KeyLabel` |
| `OpenWebAction` | `openWeb` | `Url` |
| `MouseAction` | `qmk_mouse` | `MouseKey`, `MouseEvent`, `MouseX`/`Y`/`VerticalScroll`/`HorizontalScroll` |
| `PageSwitchAction` | `pageSwitch` | `PageSwitchMode` (1=previous, 2=next), `JumpToPage` |
| `OpenPageAction` | `openPage` | `PageName` (target page GUID) |
| `OneLevelUpAction` | `oneLevelUp` | — |
| `TextInputAction` | `text` | `InputText`, `IsInputEnter`, `IsCopyPaste` |
| `AudioVolumeAction` | `Microphone`/`Loudspeaker` | `DeviceClass`, `TargetDeviceName`, `VolumeAdjustMode`/`Value` |
| `KeyboardSwitchAction` | `keyboard_switch` | — |
| `EncoderKeyboardAction` | `encoder_keyboard` | `LeftKeycode`/`MiddleKeycode`/`RightKeycode` (+ labels) |
| `EncoderFunctionAction` | `encoder_system_volume`/etc. | `RelatedThemePath` |
| `ControlFlowAction` | `ControlFlow` | `ControlDataList` (raw bytes; populated-step schema unconfirmed) |
| `UnknownKeyAction` | any other | `RawFields` only |

Every `KeyAction` exposes `RawFields: IReadOnlyDictionary<string, TaggedValue>` for any
field not yet promoted to a typed property.

---

## 5. Building a new theme from scratch

```csharp
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Codecs;

var theme = new ThemeBuilder()
    .AddPage(page => page
        .SetCanvas(640, 656)                       // confirmed real main-screen canvas
        .AddBackground(bg => bg.MainScreen("bg.mp4", File.ReadAllBytes("bg.mp4")))
        .AddKey(row: 0, column: 0, key => key
            .Icon("icon_01.png", File.ReadAllBytes("icon_01.png"))
            .Title("Vol -")
            .Action(KeyActions.Keyboard(HidKey.Digit1, "1"))))
    .Build();

byte[] fileBytes = ThemeFileCodec.Encode(theme);
File.WriteAllBytes("MyTheme.Theme", fileBytes);
```

### `ThemeBuilder`

Fluent entry point for a brand-new theme. `.AddPage(configure)` (or the no-arg
`.AddPage()` overload returning the page builder directly) adds a page; `.Build()` returns
an immutable `ThemeFile`. The first added page becomes the active page on load
(`CurrentPageId`).

### `ThemePageBuilder` (inside `.AddPage(page => ...)`)

| Method | Adds |
|---|---|
| `.SetCanvas(width, height, showUnit=true)` | Canvas size — always `640, 656` for the real main screen. Call first. |
| `.AddKey(row, col, configure)` | A physical key (`KeyItemBuilder`, see below). |
| `.AddBackground(configure)` | `.mp4` video background, main or secondary screen (`BackgroundItemBuilder`). |
| `.AddDynamicImage(configure)` | Decorative animated GIF, or (via `.MainScreenBackground(...)`/`.SecondaryScreenBackground(...)`) a picture/GIF screen background (`DynamicImageItemBuilder`). |
| `.AddText(configure)` | Static or data-bound text label. |
| `.AddProgressBar(configure)` / `.AddLinearGauge(configure)` / `.AddRadialGauge(configure)` | Data-bound gauges. |
| `.AddDigitalClockField(configure)` | One clock field (`hour`/`minute`/`second`); combine 2–3 for a full clock. |

### `KeyItemBuilder` (inside `.AddKey(row, col, key => ...)`)

| Method | Effect |
|---|---|
| `.Icon(fileName, bytes)` | Sets a static icon; auto-normalized to the required 128x128 RGB PNG format. |
| `.AnimatedIcon(folderName, gifBytes)` | Sets a multi-frame animated icon (pressable key, unlike a decorative dynamic image). |
| `.IconAssetPath(path)` | Points at an already-registered/static system asset path (e.g. `/static/icon/dark/PageSwitch.png`) instead of registering a new one. |
| `.Action(keyAction)` | Assigns behavior — build one via `KeyActions` (below). |
| `.Title(text)` | On-screen label text over the icon. |
| `.Opacity(0-100)` | Icon transparency (100 = opaque, the default). |
| `.TitleStyle(fontFamily?, fontSize?, alignment?, colorHex?)` | Overrides title font/color; only `"top"`/`"bottom"` are confirmed real `alignment` values. |
| `.IconSize(width, height)` | Overrides rendered icon size (defaults to 128x128). |
| `.At(x, y, z=1)` | Overrides auto-derived position (defaults to row/column × 128px cells from origin (0,144)). |
| `.Locked(locked=true)` | Real key items are always locked; defaults to `true`. |

### `KeyActions` factory methods

```csharp
using Mk20Control.Protocol.Theme.Building;

KeyActions.Keyboard(HidKey.A, "A")                                    // plain keystroke
KeyActions.KeyboardCombo(KeyModifiers.LeftCtrl | KeyModifiers.LeftAlt, HidKey.Delete) // Ctrl+Alt+Del
KeyActions.PreviousPage() / KeyActions.NextPage()                     // relative page nav
KeyActions.OpenPage(otherPage.PageId)                                 // jump to a specific page
KeyActions.OneLevelUp()
KeyActions.OpenWeb("https://example.com")
KeyActions.Mouse(mouseKey, mouseEvent, x, y, vScroll, hScroll)
KeyActions.TypeText("hello", pressEnterAfter: true)
KeyActions.AudioVolume(AudioDeviceClass.Loudspeaker, "Speakers", adjustMode, adjustValue)
KeyActions.KeyboardSwitch()
KeyActions.EncoderKeyboard(leftKeycode, leftLabel, middleKeycode, middleLabel, rightKeycode, rightLabel)
KeyActions.EncoderFunction("encoder_system_volume", relatedThemePath: null)
```

`HidKey` is an enum of the standard USB HID keyboard usage table (`A`-`Z`, `Digit0`-`Digit9`,
`Enter`, `Escape`, `Tab`, `Delete`, `F1`-`F12`, arrow keys, etc.) — use it instead of raw
integers. `KeyModifiers` is a `[Flags]` enum (`LeftCtrl`, `LeftShift`, `LeftAlt`, `LeftWin`,
`RightCtrl`, `RightShift`, `RightAlt`, `RightWin`) for `KeyboardCombo`.

### Screen backgrounds

```csharp
// Video background (main OR secondary screen) - the mechanism every vendor theme uses.
page.AddBackground(bg => bg.MainScreen("bg.mp4", mp4Bytes));
page.AddBackground(bg => bg.SecondaryScreen("bg.mp4", mp4Bytes));

// Picture or GIF background (confirmed alternative for BOTH main and secondary screens).
page.AddDynamicImage(img => img.MainScreenBackground("photo.jpg", jpegBytes));
page.AddDynamicImage(img => img.SecondaryScreenBackground("anim.gif", gifBytes));
```
A static image is resized/cropped to exactly fill its target area (640x512 main,
428x142 secondary); a GIF is embedded at its original, unresized size.

---

## 6. Editing an existing theme

Use `ThemeEditor` when you want to modify one or two things in an already-built or
downloaded theme without reconstructing it from scratch.

```csharp
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Building;

var editor = new ThemeEditor(ThemeFileCodec.Decode(existingBytes));

editor.Page(0).SetKeyIcon(row: 0, column: 2, "new_icon.png", File.ReadAllBytes("new_icon.png"));
editor.Page(0).SetKeyAction(row: 0, column: 2, KeyActions.TypeText("hello"));
editor.Page(0).SetKeyTitle(row: 0, column: 2, "Say Hi");
editor.Page(0).SetKeyOpacity(row: 0, column: 2, 60);
editor.Page(0).AddKey(row: 1, column: 0, key => key
    .Icon("icon_05.png", File.ReadAllBytes("icon_05.png"))
    .Action(KeyActions.Keyboard(HidKey.Digit5, "5")));
editor.Page(0).RemoveKey(row: 3, column: 4);
editor.Page(0).SetMainBackground("new_bg.mp4", File.ReadAllBytes("new_bg.mp4"));

byte[] updatedBytes = ThemeFileCodec.Encode(editor.Save());
```

| `ThemeEditor` member | Effect |
|---|---|
| `.Page(index)` / `.PageById(pageId)` | Get a `PageEditor` for one page. |
| `.PageCount` | Number of pages. |
| `.SetCurrentPage(pageId)` | Change which page opens on activation. |
| `.RegisterAsset(fileName, bytes)` / `.RegisterAssetAtPath(fullPath, bytes)` | Low-level asset registration (used internally by the item builders below; rarely needed directly). |
| `.Save()` | Returns the updated immutable `ThemeFile`. |

| `PageEditor` member (`editor.Page(n).___`) | Effect |
|---|---|
| `.FindKey(row, col)` | Returns the `KeyItem` at that position, or `null`. |
| `.SetKeyIcon(row, col, fileName, bytes)` | Replace a key's icon (auto-normalized). |
| `.SetKeyAction(row, col, action)` | Replace a key's behavior. |
| `.SetKeyTitle(row, col, text)` | Set on-screen title text. |
| `.SetKeyOpacity(row, col, percent)` | Set icon transparency. |
| `.AddKey(row, col, configure)` | Add a brand-new key (same `KeyItemBuilder` as `ThemeBuilder`). |
| `.RemoveKey(row, col)` | Remove a key if present. |
| `.SetMainBackground(fileName, bytes)` | Replace/add the main-screen video background. |
| `.Items` | All items on the page, in original order. |

All edits preserve the original item's `RawJson` except for the specific fields changed —
any field this library doesn't model yet survives round-trip untouched.

---

## 7. Uploading a theme to the device

```csharp
byte[] themeBytes = ThemeFileCodec.Encode(theme); // from ThemeBuilder or ThemeEditor
await client.UploadThemeFileAsync("/data/theme/MK20/MyTheme/MyTheme.Theme", themeBytes);
```

`UploadThemeFileAsync` performs the complete confirmed sequence internally
(`GET_DEVICE_THEME` → abort-transfer → `FILE_START` → 4096-byte bulk chunks → `FILE_END` →
abort-transfer → `SET_DEVICE_RELOAD`), retries once on a `FILE_END`/reload timeout after
confirming the device is still alive (fails fast with a clear message if not — a physical
power-cycle is then required), and automatically normalizes the theme's `currentPage` to
its first page before sending, so activation always lands on page 1 regardless of what was
embedded in the source bytes.

Device-side paths follow the convention `/data/theme/MK20/<name>/<name>.Theme` (matching
the vendor app), though any valid path the device accepts works.

---

## 8. Typical integration pattern (e.g. a SimHub plugin)

```csharp
// 1. Connect once at plugin startup.
var client = Mk20DeviceClient.CreateForSerialPort("COM7", loggerFactory: pluginLogger);
await client.ConnectAsync();

// 2. Build (or load+edit) a theme representing your app's button layout, upload once.
var theme = new ThemeBuilder()
    .AddPage(page => page
        .SetCanvas(640, 656)
        .AddKey(0, 0, key => key.Icon("pit_limiter.png", pitLimiterIconBytes)
            .Title("Pit Limiter").Action(KeyActions.Keyboard(HidKey.P))))
    .Build();
await client.UploadThemeFileAsync("/data/theme/MK20/SimHub/SimHub.Theme", ThemeFileCodec.Encode(theme));

// 3. React to button presses via the event.
client.NotificationReceived += (_, e) =>
{
    if (e.Position.Row == 0 && e.Position.Column == 0 && e.IsPressed)
        TogglePitLimiter();
};

// 4. Periodically push live telemetry your theme's gauges/text are bound to.
var timer = new System.Timers.Timer(500);
timer.Elapsed += async (_, _) => await client.PushSystemDataAsync(new Dictionary<string, string>
{
    ["Speed"] = currentSpeedKph.ToString(),
});
timer.Start();

// 5. Clean up on plugin shutdown.
await client.DisconnectAsync();
await client.DisposeAsync();
```

To change a button's icon/function at runtime (e.g. reflecting a changed car/session
state), decode the currently-installed theme, edit it with `ThemeEditor`, and re-upload —
there is no live per-key command; every change requires re-sending the whole file (see
protocol spec §6.4).

---

## 9. Error handling

All client operations throw `Mk20Control.Protocol.Exceptions.Mk20ProtocolException` (or a
subtype) on failure:

| Exception | Thrown when |
|---|---|
| `Mk20TimeoutException` | No matching reply received within the timeout. |
| `Mk20ChecksumException` | A received frame's payload failed CRC-32 validation. |
| `Mk20UnconfirmedOperationException` | An operation depends on unconfirmed protocol behavior and wasn't explicitly opted into. |
| `InvalidOperationException` | `DeleteThemeAsync` called on a path with an unconfirmed pending reload (safety guard, not a protocol error). |

`ThemeFileCodec.Decode` throws `System.IO.InvalidDataException`/`FormatException` for a
malformed `.Theme` file rather than a protocol exception, since decoding is independent of
any live device.

---

## 10. Diagnostics

`Mk20Control.Protocol.Transport.WireLoggingTransport` wraps any `ISerialTransport` and logs
every byte written/read to a file — a live-capture substitute useful for comparing this
client's wire behavior against a genuine Wireshark/USBPcap capture of ScreenKeyWindows when
diagnosing an integration issue.

```csharp
var inner = new SerialPortTransport("COM7");
var logging = new WireLoggingTransport(inner, "wire-log.txt");
var client = new Mk20DeviceClient(logging);
```

---

*See `PROTOCOL_WAVESHARE_MK20.md` for the full wire protocol and `.Theme` file format
specification this library implements.*
