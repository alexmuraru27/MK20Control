# Mk20Control.Protocol — API Reference

**Target framework:** .NET 9
**Assembly:** `Mk20Control.Protocol` (project reference or build+reference the DLL)
**Purpose:** reusable client library for the Waveshare MK20 programmable keypad — connect
to the device, read/build/edit `.Theme` files, and drive its display and telemetry
programmatically from any .NET application (e.g. a SimHub plugin, a custom button-box
controller, or any app that wants to reflect its own state on the device's buttons).

For the underlying wire protocol and file format specification, see
[`PROTOCOL_WAVESHARE_MK20.md`](./PROTOCOL_WAVESHARE_MK20.md). For build/run instructions,
project layout, and the full derivation history behind every confirmed protocol fact, see
[`README.md`](./README.md). This document covers only the library's public API surface and
how to use it.

---

## 1. Package layout

| Namespace | Purpose |
|---|---|
| `Mk20Control.Protocol.Client` | `Mk20DeviceClient` — connect, control, and receive events from a physical device |
| `Mk20Control.Protocol.Transport` | Serial transport abstraction (`ISerialTransport`, `SerialPortTransport`) |
| `Mk20Control.Protocol.Model` | Value types returned by the client (`DeviceIdentity`, `ThemeListing`, `KeyPosition`, `CommandId`) |
| `Mk20Control.Protocol.Codecs` | `ThemeFileCodec` — decode/encode raw `.Theme` file bytes |
| `Mk20Control.Protocol.Theme` | `ThemeFile`, `ThemePage`, `ThemeCanvas`, `ThemeAsset` — the decoded theme data model |
| `Mk20Control.Protocol.Theme.Items` | Core page item types (`KeyItem`, `BackgroundItem`, `DynamicImageItem`) |
| `Mk20Control.Protocol.Theme.Items.Widgets` | Data-bound widget item types (`TextItem`, `MultilineTextItem`, `ShadowTextItem`, `ProgressBarItem`, `LinearGaugeItem`, `RadialGaugeItem`, `CircularGaugeItem`, `SegmentedCircularGaugeItem`, `LightShadowGaugeItem`, `DigitalClockItem`) |
| `Mk20Control.Protocol.Theme.Actions` | Key action types (`KeyboardAction`, `PageSwitchAction`, etc.) |
| `Mk20Control.Protocol.Theme.Building` | `ThemeBuilder`, `ThemeEditor`, `ThemePageBuilder`, `KeyActions`, `HidKey`, `KeyModifiers`, `EncoderFunctionType` — fluent theme construction/editing |
| `Mk20Control.Protocol.Theme.Building.Widgets` | Fluent builders for the widget item types above (`TextItemBuilder`, `ProgressBarItemBuilder`, `RadialGaugeItemBuilder`, etc.) |
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
Key names are theme-defined (bound via `system_data_name` in the theme's JSON, set through
each widget builder's `.BoundTo(...)` — see §5.5) — push whatever keys the currently
loaded theme declares via its `deviceRequestSystemData` contract (sent automatically after
every `SET_DEVICE_RELOAD`). There is no fixed/reserved key set: any string you bind a
widget to in the theme is a valid key to push, including custom ones like `"Speed"` or
`"LapTime"`. Most integrations simply push their own known key set on a timer.

Confirmed real-hardware convention: values are pushed as **pre-formatted display
strings**, not bare numbers — even for a widget bound with a numeric `min`/`max` range
(e.g. `"22%"`, `"61℃"`, `"20 GB"`). The device/renderer parses the leading numeric portion
for gauge fill level and displays the whole string as text; a non-numeric string on a
numeric-bound gauge does not error, it is simply not usable as a fill percentage.

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
│   ├── ParentPageName (GUID string; present ONLY on folder sub-pages — this is what makes a page a folder)
│   ├── Canvas: ThemeCanvas (Width, Height, IsFlipped, IsRotated, ShowUnit)
│   ├── Encoder: JsonElement? (rotary-encoder hardware descriptor, always present on a main-screen page)
│   └── Items: IReadOnlyList<ThemeItem>
│       ├── KeyItem (type 115): Row, Column, IconAssetPath, Action, RawControlDataBase64
│       ├── BackgroundItem (type 100): Surface, AssetPath (`.mp4` video only)
│       ├── DynamicImageItem (type 114): AssetPath, SystemDataName, BackgroundType ("main"/"secondary"/null)
│       ├── TextItem (113), MultilineTextItem (116), ShadowTextItem (117)
│       ├── ProgressBarItem (102), LinearGaugeItem (103), RadialGaugeItem (109)
│       ├── CircularGaugeItem (101), SegmentedCircularGaugeItem (104), LightShadowGaugeItem (110)
│       ├── DigitalClockItem (111)
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
| `PageSwitchAction` | `pageSwitch` | `PageSwitchMode` (1=previous, 2=next, 0=absolute jump), `JumpToPage` (zero-based page index, used when mode is 0) |
| `OpenPageAction` | `openPage` | `PageName` (target page GUID) |
| `OneLevelUpAction` | `oneLevelUp` | `PageName` (always the sentinel `"parentPage"`, which resolves via the page's own `ParentPageName`) |
| `TextInputAction` | `text` | `InputText`, `IsInputEnter`, `IsCopyPaste` |
| `AudioVolumeAction` | `Microphone`/`Loudspeaker` | `DeviceClass`, `TargetDeviceName`, `VolumeAdjustMode`/`Value` |
| `KeyboardSwitchAction` | `keyboard_switch` | — |
| `EncoderKeyboardAction` | `encoder_keyboard` | `LeftKeycode`/`MiddleKeycode`/`RightKeycode` (+ labels) |
| `EncoderFunctionAction` | `encoder_system_volume`/`encoder_device_brightness`/`encoder_system_media` | `RelatedThemePath`. Build via `KeyActions.EncoderFunction(EncoderFunctionType.___)`. |
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
| `.AsFolderOf(parentPage)` | Marks this page as a **folder** of `parentPage` (emits `parentPageName`). Required for `KeyActions.OneLevelUp()` to work — see [Page navigation](#page-navigation-paging-jumps-and-folders). |
| `.AddKey(row, col, configure)` | A physical key (`KeyItemBuilder`, see below). |
| `.AddBackground(configure)` | `.mp4` video background, main or secondary screen (`BackgroundItemBuilder`). |
| `.AddDynamicImage(configure)` | Decorative animated GIF, or (via `.MainScreenBackground`/`.SecondaryScreenBackground`/their `AutoFit` size-guarding variants) a picture/GIF screen background (`DynamicImageItemBuilder`). |
| `.AddText(configure)` | Static or data-bound text label (type 113). |
| `.AddMultilineText(configure)` | Static or data-bound wrapping text block (type 116). |
| `.AddShadowText(configure)` | Static or data-bound text with border stroke + drop-shadow (type 117). |
| `.AddProgressBar(configure)` / `.AddLinearGauge(configure)` / `.AddRadialGauge(configure)` | Data-bound bar/gauge (types 102/103/109). |
| `.AddCircularGauge(configure)` / `.AddSegmentedCircularGauge(configure)` | Data-bound plain or segmented ring gauge, no gradient/angle range (types 101/104). |
| `.AddLightShadowGauge(configure)` | Data-bound ring with a separate arc stroke plus a glow/shadow highlight (type 110). |
| `.AddDigitalClockField(configure)` | One clock field (`hour`/`minute`/`second`); combine 2–3 for a full clock (type 111). |

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
KeyActions.PreviousPage() / KeyActions.NextPage()                     // relative page nav (ring)
KeyActions.JumpToPage(2)                                              // absolute jump to page index 2
KeyActions.OpenPage(otherPage.PageId)                                 // enter a "folder" page (by GUID)
KeyActions.OneLevelUp()                                               // return out of a folder
KeyActions.OpenWeb("https://example.com")
KeyActions.Mouse(mouseKey, mouseEvent, x, y, vScroll, hScroll)
KeyActions.TypeText("hello", pressEnterAfter: true)
KeyActions.AudioVolume(AudioDeviceClass.Loudspeaker, "Speakers", adjustMode, adjustValue)
KeyActions.KeyboardSwitch()
KeyActions.EncoderKeyboard(leftKeycode, leftLabel, middleKeycode, middleLabel, rightKeycode, rightLabel)
KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume, relatedThemePath: null)  // strongly typed
KeyActions.EncoderFunction("encoder_system_volume", relatedThemePath: null)          // raw-string fallback
```

`HidKey` is an enum of the standard USB HID keyboard usage table (`A`-`Z`, `Digit0`-`Digit9`,
`Enter`, `Escape`, `Tab`, `Delete`, `F1`-`F12`, arrow keys, etc.) — use it instead of raw
integers. `KeyModifiers` is a `[Flags]` enum (`LeftCtrl`, `LeftShift`, `LeftAlt`, `LeftWin`,
`RightCtrl`, `RightShift`, `RightAlt`, `RightWin`) for `KeyboardCombo`.

### Page navigation: paging, jumps and folders

A theme's pages are a **flat array** — there is no nesting in the file format. What makes a
page a "folder" is simply that some key opens it. There are three confirmed mechanisms, and
they can be mixed freely on the same page.

| Goal | Factory | Wire form |
|---|---|---|
| Previous page (relative) | `KeyActions.PreviousPage()` | `pageSwitchMode=1` |
| Next page (relative) | `KeyActions.NextPage()` | `pageSwitchMode=2` |
| Jump to a page (absolute) | `KeyActions.JumpToPage(index)` | `pageSwitchMode=0` + `jumpToPage=<index>` |
| Enter a folder | `KeyActions.OpenPage(pageId)` | `openPage` + `pageName=<page GUID>` |
| Return from a folder | `KeyActions.OneLevelUp()` | `oneLevelUp` + `pageName="parentPage"` |

Two things to keep straight:

- **`JumpToPage` takes a zero-based page *index*; `OpenPage` takes a page *GUID*.** Use the
  no-arg `.AddPage()` overload to capture a `ThemePageBuilder` and pass its `.PageId`.
- **`OpenPage` alone does not make a folder.** The target page must also declare its parent
  via `.AsFolderOf(parent)` — see below. Without it the device navigates *into* the page and
  then refuses to leave.

Navigation keys normally reuse the vendor's built-in artwork instead of embedding an icon —
see `SystemIconPaths` (`PageSwitch`, `CreateFolder`, `OneLevelUp`, plus the smaller
`*Glyph` variants used as an action's own `iconPath`):

```csharp
key.IconAssetPath(SystemIconPaths.CreateFolder).Action(KeyActions.OpenPage(folder.PageId));
```

#### Relative paging (a ring)

Every page gets a previous/next pair, so paging wraps around — the last page's "next"
returns to the first, with no special-casing, because both actions are always relative to
whichever page is currently shown.

```csharp
var builder = new ThemeBuilder();
for (int i = 0; i < 6; i++)
{
    builder.AddPage(page =>
    {
        page.SetCanvas(640, 656);
        page.AddKey(3, 0, key => key
            .IconAssetPath(SystemIconPaths.PageSwitch)
            .Title("PREV")
            .Action(KeyActions.PreviousPage()));
        page.AddKey(3, 4, key => key
            .IconAssetPath(SystemIconPaths.PageSwitch)
            .Title("NEXT")
            .Action(KeyActions.NextPage()));
    });
}
```

#### Absolute jumps (hub and spoke)

This is how the vendor's own `defaultTheme.Theme` navigates — it contains no relative paging
at all. A home page jumps out to each section, and each section jumps back to index 0.

```csharp
var builder = new ThemeBuilder();
var hub = builder.AddPage().SetCanvas(640, 656);      // page index 0
var fuel = builder.AddPage().SetCanvas(640, 656);     // page index 1
var tyres = builder.AddPage().SetCanvas(640, 656);    // page index 2

hub.AddKey(0, 0, key => key.Title("FUEL").Action(KeyActions.JumpToPage(1)));
hub.AddKey(0, 1, key => key.Title("TYRES").Action(KeyActions.JumpToPage(2)));

foreach (var section in new[] { fuel, tyres })
{
    section.AddKey(3, 4, key => key
        .IconAssetPath(SystemIconPaths.PageSwitch)
        .Title("HOME")
        .Action(KeyActions.JumpToPage(0)));           // 0 = the hub's index
}
```

### How foldering actually works

A folder is an ordinary page plus **one page-level field**: `parentPageName`, holding the
`pageName` (GUID) of the page it hangs off. That single field is what separates a folder
from a normal page — everything else (canvas, grid, keys) is identical.

```json
// an ordinary page                 // a folder page
{ "canvas": {...},                  { "canvas": {...},
  "encoder": [...],                   "encoder": [...],
  "items":  [...],                    "items":  [...],
  "pageName": "<guid>" }              "pageName": "<guid>",
                                      "parentPageName": "<parent's guid>" }
```

The two halves work together:

| Direction | Mechanism |
|---|---|
| In | A key with `KeyActions.OpenPage(folder.PageId)` — targets the folder by GUID |
| Out | A key with `KeyActions.OneLevelUp()`, which emits the fixed sentinel `pageName="parentPage"` |

`"parentPage"` is **not** a page id. It means *"go to the page named by my page's
`parentPageName`"*. So the return destination comes from the page, not from the key — which
is why `OneLevelUp()` needs no arguments, and why every return key in every real theme is
byte-identical regardless of depth.

> **The failure mode to know about.** If you call `OpenPage` at a page that never declared a
> parent, the device happily navigates *into* it and then will not come back out. The return
> key is received and correctly decoded as `oneLevelUp` — the device even reports the press
> to the host — but nothing happens, because the page has no `parentPageName` to resolve.
> Confirmed on real hardware. Use `.AsFolderOf(...)` and this can't happen.

#### Folders, and returning to the base folder

Create the folder page, mark it with `.AsFolderOf(parent)`, then point a key at it. Real
themes place the return key at the bottom-right cell (row 3, column 4).

```csharp
var builder = new ThemeBuilder();
var home = builder.AddPage().SetCanvas(640, 656);
var folder = builder.AddPage().SetCanvas(640, 656)
    .AsFolderOf(home);                                // <- emits parentPageName; REQUIRED

home.AddKey(0, 0, key => key
    .IconAssetPath(SystemIconPaths.CreateFolder)
    .Title("PIT")
    .Action(KeyActions.OpenPage(folder.PageId)));     // in

folder.AddKey(3, 4, key => key
    .IconAssetPath(SystemIconPaths.OneLevelUp)
    .Title("BACK")
    .Action(KeyActions.OneLevelUp()));                // back out to `home`
```

#### Putting content in a folder

A folder page takes keys exactly like any other page — same 4x5 grid, same `AddKey(row, col, …)`,
same actions. The only convention worth keeping is reserving the bottom-right cell for the
return key, which leaves 19 usable cells:

```csharp
var pit = builder.AddPage().SetCanvas(640, 656).AsFolderOf(home);

// Fill the grid in reading order, skipping the reserved return cell.
var functions = new (string Label, HidKey Key)[]
{
    ("PIT REQ", HidKey.A), ("FUEL +", HidKey.B), ("FUEL -", HidKey.C),
    ("TYRES",   HidKey.D), ("REPAIR", HidKey.E),
};

int i = 0;
foreach (var (label, hidKey) in functions)
{
    int row = i / 5, col = i % 5;
    i++;
    pit.AddKey(row, col, key => key
        .Icon($"{label}.png", File.ReadAllBytes(iconPath))
        .Title(label)
        .Action(KeyActions.Keyboard(hidKey, hidKey.ToString())));
}

pit.AddKey(3, 4, key => key                            // reserved: return key
    .IconAssetPath(SystemIconPaths.OneLevelUp)
    .Title("BACK")
    .Action(KeyActions.OneLevelUp()));
```

A folder page can also hold `JumpToPage` keys (e.g. a "HOME" shortcut straight back to index
0), backgrounds, gauges and text — it is a full page in every respect.

#### Nested folders

Nesting is just chaining: each level is a folder *of the level above it*, and each level's
key opens the next. Depth is unlimited — a real vendor theme was found nested five deep.

```csharp
var builder = new ThemeBuilder();
var root = builder.AddPage().SetCanvas(640, 656);

// root -> level 1 -> level 2 -> level 3
var pages = new List<ThemePageBuilder> { root };
for (int level = 1; level <= 3; level++)
{
    var page = builder.AddPage().SetCanvas(640, 656)
        .AsFolderOf(pages[level - 1]);                 // parent is the level above
    pages.Add(page);
}

for (int level = 0; level < pages.Count; level++)
{
    if (level + 1 < pages.Count)                       // a way down...
    {
        var child = pages[level + 1];
        pages[level].AddKey(0, 0, key => key
            .IconAssetPath(SystemIconPaths.CreateFolder)
            .Title($"LEVEL {level + 1}")
            .Action(KeyActions.OpenPage(child.PageId)));
    }

    if (level > 0)                                     // ...and a way back up
    {
        pages[level].AddKey(3, 4, key => key
            .IconAssetPath(SystemIconPaths.OneLevelUp)
            .Title("BACK")
            .Action(KeyActions.OneLevelUp()));
    }
}
```

`OneLevelUp` moves exactly **one** level per press, matching vendor behaviour. To escape to
the top from deep inside, add a single `KeyActions.JumpToPage(0)` key instead — it jumps
straight to page index 0 regardless of depth.

> Working end-to-end examples: `OfflineThemeTests/NavigationThemeBuilderTests.cs` builds one
> theme using all four navigation styles, and
> `OfflineThemeTests/NestedFolderThemeBuilderTests.cs` builds a configurable-depth chain and
> asserts every level's parent link. `HardwareTests/NavigationThemeUploadTests.cs` uploads to
> a real device; `HardwareTests/ListenForEventsTests.cs` reports each press alongside the
> device's own `themePageSwitch` confirmation, which is the reliable way to verify navigation
> actually happened.

### Screen backgrounds

```csharp
// Video background (main OR secondary screen) - the mechanism every vendor theme uses.
page.AddBackground(bg => bg.MainScreen("bg.mp4", mp4Bytes));
page.AddBackground(bg => bg.SecondaryScreen("bg.mp4", mp4Bytes));

// Picture or GIF background (confirmed alternative for BOTH main and secondary screens).
// These register the bytes as-is - pre-size the source yourself to exactly 640x512 (main)
// or 428x142 (secondary), matching every real background asset examined.
page.AddDynamicImage(img => img.MainScreenBackground("photo.jpg", jpegBytes));
page.AddDynamicImage(img => img.SecondaryScreenBackground("anim.gif", gifBytes));

// Auto-fit variants: resize/crop an arbitrary source image/GIF to the exact required size
// for you (via BackgroundImageNormalizer), with optional pan/offset control.
page.AddDynamicImage(img => img.MainScreenBackgroundAutoFit("photo.jpg", anySizeJpegBytes));
page.AddDynamicImage(img => img.SecondaryScreenBackgroundAutoFit("anim.gif", anySizeGifBytes,
    offsetXPercent: -1, offsetYPercent: 0)); // pan the crop window fully left
```
`offsetXPercent`/`offsetYPercent` are each in `[-1, 1]` (0 = centered crop, the default; -1 =
crop window as far left/up as possible, +1 as far right/down as possible) - useful when the
source's aspect ratio doesn't match the target and you want to control which part of the
image survives the crop (e.g. keep the top of a GIF visible instead of its center). This
only affects which source pixels are kept; it does not change the item's on-device
x/y/w/h rectangle. Animated GIF sources keep their frame count/delays/loop count.

### Physical rotary encoders

The MK20 has two physical rotary encoders on the secondary screen. Confirmed real-hardware
layout (cross-checked against `defaultTheme.Theme` and `海边吹风.Theme`): an encoder
function is a normal `KeyItem` positioned at a fixed coordinate — not part of the row/column
key grid — with its action set via `KeyActions.EncoderFunction(...)`:

| Encoder | Fixed position |
|---|---|
| Left | `x=106, y=0` |
| Right | `x=320, y=0` |

```csharp
using Mk20Control.Protocol.Theme.Building;

// Left encoder -> system volume
page.AddKey(0, 0, key => key
    .At(106, 0)
    .IconAssetPath("/static/icon/white/systemVolume_.png")
    .Action(KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)));

// Right encoder -> device (screen) brightness
page.AddKey(0, 0, key => key
    .At(320, 0)
    .IconAssetPath("/static/icon/white/deviceBrightness_.png")
    .Action(KeyActions.EncoderFunction(EncoderFunctionType.DeviceBrightness)));
```

`EncoderFunctionType` (`SystemVolume`, `DeviceBrightness`, `SystemMedia`) is a strongly-typed
enum for the confirmed real function-type strings; a raw `string rawType` overload of
`EncoderFunction` remains available for any future/unconfirmed function type.

A live progress-bar/text readout of the current value can optionally be placed near the
encoder (bound to `"Volume"`/`"device_bl"` — see §5.5), matching the real vendor theme
layout. The encoder function works regardless of whether anything is visibly rendered — set
the key's icon to `.Opacity(0)` and any accompanying progress-bar/text colors to a fully
transparent `"r=0,g=0,b=0,a=0"` if you don't want anything shown on the secondary screen:

```csharp
page.AddKey(0, 0, key => key.At(106, 0).IconAssetPath("/static/icon/white/systemVolume_.png")
    .Opacity(0) // fully invisible, encoder still works
    .Action(KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)));
page.AddProgressBar(pb => pb.At(204, 96, 100, 12).BoundTo("Volume", 0, 100)
    .Colors("r=0,g=0,b=0,a=0", "r=0,g=0,b=0,a=0", "r=0,g=0,b=0,a=0"));
```

Confirmed working on real hardware in both variants (visible and invisible) — see
`src/Mk20Control.IntegrationTests/OfflineThemeTests/EncoderVolumeAndBrightnessThemeTests.cs`
for a complete, runnable example, and `HardwareTests/EncoderVolumeAndBrightnessUploadTests.cs`
for the upload+live-telemetry-pump variant.

### Widgets — gauges, text, and clocks

Beyond keys and backgrounds, a page can carry any number of display widgets — progress
bars, gauges, text, and clock fields — each optionally data-bound via `.BoundTo(...)` and
updated at runtime with `PushSystemDataAsync` (§3). Builders live in
`Mk20Control.Protocol.Theme.Building.Widgets`; resulting item types in
`Mk20Control.Protocol.Theme.Items.Widgets`.

| Widget | `ThemePageBuilder` method | Item type | Key configuration methods |
|---|---|---|---|
| Progress bar | `.AddProgressBar(configure)` | 102 | `.At(x, y, w, h, z=1)`, `.BoundTo(name, min=0, max=100)`, `.Colors(front, back, border, borderWidth=2, cornerRadius=5)` |
| Linear gauge | `.AddLinearGauge(configure)` | 103 | `.At(x, y, w, h, z=1)`, `.BoundTo(name, min, max)`, `.Colors(front, back, border, borderWidth=2)` |
| Radial gauge | `.AddRadialGauge(configure)` | 109 | `.At(x, y, z=1, scale=0.5)`, `.BoundTo(name, min, max)`, `.AngleRange(minDeg=225, maxDeg=315)`, `.Gradient(c1, c2?, c3?)`, `.Direction(clockwise=true)` |
| Circular gauge | `.AddCircularGauge(configure)` | 101 | `.At(x, y, z=1)`, `.BoundTo(name, min, max)`, `.Colors(front, back)`, `.Geometry(margin=20, radius=100)` |
| Segmented circular gauge | `.AddSegmentedCircularGauge(configure)` | 104 | Same as circular gauge (identical JSON shape; renders as a segmented/notched ring) |
| Light-shadow gauge | `.AddLightShadowGauge(configure)` | 110 | `.At(x, y, z=1)`, `.BoundTo(name, min, max)`, `.Colors(back, arc, arcWidth=6)`, `.Geometry(radius=50, clockwise=true, displayDirection=1)`, `.LightShadow(color, lighter=100, position=80)` |
| Text | `.AddText(configure)` | 113 | `.At(x, y, z=1)`, `.Text(string)` or `.BoundTo(name)`, `.Font(descriptor, scale=1)`, `.Color(rgba)` |
| Multiline text | `.AddMultilineText(configure)` | 116 | `.At(x, y, w=200, h=100, z=1)`, `.Text(...)`/`.BoundTo(name)`, `.Font(descriptor)`, `.Color(rgba)` |
| Shadow text | `.AddShadowText(configure)` | 117 | `.At(x, y, z=1)`, `.Text(...)`/`.BoundTo(name)`, `.Font(descriptor)`, `.Color(rgba)`, `.Border(rgba, width=5)`, `.Shadow(rgba, size=10)` |
| Digital clock field | `.AddDigitalClockField(configure)` | 111 | `.At(x, y, w=128, h=128, z=1)`, `.Field("hour"\|"minute"\|"second", displayDigits=2)`, `.Font(descriptor)`, `.Colors(front, back, border)` |

Colors are `"r=<0-255>,g=<0-255>,b=<0-255>,a=<0-255>"` strings throughout. Font descriptors
follow the confirmed real format `"family,size,-1,5,weight,0,0,0,0,0[,style]"` (e.g.
`"Microsoft YaHei,20,-1,5,50,0,0,0,0,0"`).

**The digital clock is host-driven, not device-RTC-driven.** Confirmed via a real capture
(`tools/Captures/capture17_multiple_theme_set.pcapng`): ScreenKeyWindows pushes `hour`,
`minute`, and `second` through `SEND_SYSTEM_DATA_TO_DEVICE` once per second, exactly like
any other telemetry value — the device does not keep its own clock. A clock widget in your
theme will show a static `00:00` (or whatever it was last set to) unless your application
pushes these three keys on a timer, same as any other gauge.

```csharp
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Building.Widgets;
using Mk20Control.Protocol.Codecs;

var theme = new ThemeBuilder()
    .AddPage(page => page
        .SetCanvas(640, 656)
        .AddProgressBar(pb => pb.At(20, 20, 200, 30).BoundTo("CPU Usage", 0, 100)
            .Colors("r=0,g=170,b=255,a=220", "r=255,g=255,b=255,a=140", "r=0,g=0,b=0,a=180"))
        .AddText(t => t.At(20, 55).BoundTo("CPU Usage").Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0"))
        .AddRadialGauge(rg => rg.At(300, 20, scale: 0.4).BoundTo("GPU Usage", 0, 100)
            .Gradient("r=0,g=170,b=255,a=255", "r=255,g=200,b=0,a=255", "r=255,g=0,b=0,a=255"))
        .AddDigitalClockField(c => c.At(500, 20, 40, 40).Field("hour"))
        .AddDigitalClockField(c => c.At(545, 20, 40, 40).Field("minute")))
    .Build();

await client.UploadThemeFileAsync("/data/theme/MK20/dashboard/dashboard.Theme", ThemeFileCodec.Encode(theme));

// Push live values every second - the clock fields need this too (host-driven, not RTC-driven).
var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
while (await timer.WaitForNextTickAsync())
{
    var now = DateTime.Now;
    await client.PushSystemDataAsync(new Dictionary<string, string>
    {
        ["CPU Usage"] = $"{GetCpuUsagePercent()}%",
        ["GPU Usage"] = $"{GetGpuUsagePercent()}%",
        ["hour"] = now.Hour.ToString(),
        ["minute"] = now.Minute.ToString(),
    });
}
```

See `src/Mk20Control.IntegrationTests/OfflineThemeTests/MainScreenAllWidgetTypesThemeTests.cs`
for a complete, runnable example exercising every single widget type at once (each bound to
its own test channel, `test1`-`test9`, plus a live clock), and
`HardwareTests/MainScreenAllWidgetTypesUploadTests.cs` for the live-hardware variant that
pumps varied random/ramp/sine-wave values so each widget's live update behavior can be
visually verified — run it with `dotnet test --environment MK20_COM_PORT=COM7
--environment MK20_UPLOAD_DEVICE_PATH=/data/theme/MK20/widgettest/widgettest.Theme`.

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
            .Title("Pit Limiter").Action(KeyActions.Keyboard(HidKey.P)))
        // A speed gauge on the secondary screen - system_data_name is your own choice.
        .AddProgressBar(pb => pb.At(20, 20, 200, 30).BoundTo("Speed", 0, 300)
            .Colors("r=0,g=170,b=255,a=220", "r=255,g=255,b=255,a=140", "r=0,g=0,b=0,a=180")))
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

*See [`PROTOCOL_WAVESHARE_MK20.md`](./PROTOCOL_WAVESHARE_MK20.md) for the full wire
protocol and `.Theme` file format specification this library implements, and
[`README.md`](./README.md) for build/run instructions and project layout.*
