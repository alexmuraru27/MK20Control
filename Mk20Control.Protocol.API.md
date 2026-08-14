# Mk20Control.Protocol — API Reference

**Target framework:** .NET 9
**Assembly:** `Mk20Control.Protocol` (project reference or build+reference the DLL)
**Purpose:** reusable client library for the Waveshare MK20 programmable keypad — connect
to the device, read/build/edit `.Theme` files, and drive its display and telemetry
programmatically from any .NET application.

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
| `Mk20Control.Protocol.Host` | `KeyBindings` — run your own C# when a physical button is pressed |
| `Mk20Control.Protocol.Theme.Items` | Core page item types (`KeyItem`, `BackgroundItem`, `DynamicImageItem`) |
| `Mk20Control.Protocol.Theme.Items.Widgets` | Data-bound widget item types (`TextItem`, `MultilineTextItem`, `ShadowTextItem`, `ProgressBarItem`, `LinearGaugeItem`, `RadialGaugeItem`, `CircularGaugeItem`, `SegmentedCircularGaugeItem`, `LightShadowGaugeItem`, `DigitalClockItem`) |
| `Mk20Control.Protocol.Theme.Actions` | Key action types (`KeyboardAction`, `PageSwitchAction`, etc.) |
| `Mk20Control.Protocol.Theme.Building` | `ThemeBuilder`, `ThemeEditor`, `ThemePageBuilder`, `KeyItemBuilder`, `KeyActions`, `ThemeColor`, `HidKey`, `KeyModifiers`, `EncoderSide`, `EncoderPositions`, `EncoderFunctionType`, `SystemIconPaths` — fluent theme construction/editing |
| `Mk20Control.Protocol.Theme.Building.Widgets` | Fluent builders for the widget item types above (`TextItemBuilder`, `ProgressBarItemBuilder`, `RadialGaugeItemBuilder`, etc.) |
| `Mk20Control.Protocol.Framing` | Wire-frame primitives (`DeviceFrame`, `DeviceFrameParser`) — needed only to build a custom transport or analyse raw captures |
| `Mk20Control.Protocol.Checksums` | `Crc32` (zlib variant) as used by the frame header |
| `Mk20Control.Protocol.Exceptions` | `Mk20ProtocolException` and subtypes |

Package dependencies (declared by the project, transitive when referencing the built DLL):
`System.Text.Json` 10.0.11, `System.IO.Ports` 10.0.11,
`Microsoft.Extensions.Logging.Abstractions` 10.0.0, `SixLabors.ImageSharp` 3.1.11.

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
integrating into a host application that has its own logging pipeline.

```csharp
var client = Mk20DeviceClient.CreateForSerialPort("COM7", loggerFactory: myLoggerFactory);
```

### Events

```csharp
client.NotificationReceived += (_, e) =>
{
    // e.Position.Row / e.Position.Column, e.IsPressed
    // e.Action           - strongly typed KeyAction (pattern-match on it)
    // e.ActionDescriptor - the same thing as raw tagged-value fields
    if (e.IsPressed && e.Action is TextInputAction text)
        Console.WriteLine($"key wants to type: {text.InputText}");
};
client.PageSwitched += (_, _) =>
{
    // The device changed page (relative paging, jumpToPage, folder in/out).
};
client.JsonReceived += (_, json) =>
{
    // Raw SEND_JSON status pushed by the device.
};
client.TransportError += (_, ex) =>
{
    // Read-loop error - connection may still be usable.
};
```

`NotificationReceived` fires for every physical key press/release **that has a bound
action in the currently loaded theme** — a key with no assigned action produces no event
at all (see the protocol spec §6.3). This is how you observe button presses to trigger
your own application logic.

`PageSwitched` is the device's own confirmation that navigation actually happened, which
makes it the reliable way to verify page/folder keys on real hardware.

---

## 3. Handling input

### 3.1 Which actions the device executes

| Action | Executed by | Notes |
|---|---|---|
| `keyboard`, `KeyboardCombo` | Device | Native USB HID; works with no software running on the PC |
| `pageSwitch`, `openPage`, `oneLevelUp` | Device | Changes page itself and reports `themePageSwitch` |
| `EncoderFunction(...)` | Device | Volume, brightness and media are handled on-device |
| `EncoderKeyboard(...)` | Device | Emits a keystroke per motion; reports nothing on the serial channel |
| `Command(id)` | **Your application** | The device emits no HID input at all — it reports the press with the ID and takes no other action |

### 3.2 Running your own code on a button press

Give the key a **command ID** when you build the theme, then bind a handler to that ID at
runtime. The ID is any string meaningful to your application.

```csharp
using Mk20Control.Protocol.Host;

// Build: stamp an ID onto the key.
page.AddKey(0, 0, key => key.Title("Build").Action(KeyActions.Command("build.start")));
page.AddKey(0, 1, key => key.Title("Mute").Action(KeyActions.Command("audio.mute")));

// Run: bind your code to that ID.
using var buttons = new KeyBindings(client);

buttons.OnCommand("build.start", () => StartBuild());
buttons.OnCommand("audio.mute", () => SetMuted(true));
buttons.OnCommandRelease("audio.mute", () => SetMuted(false));

buttons.Unbound += (_, ctx) => Log($"unhandled {ctx.CommandId ?? "(none)"} at {ctx.Position}");
```

| Member | Purpose |
|---|---|
| `OnCommand(id, handler)` / `OnCommandRelease(id, handler)` | Bind a handler; `Action` and `Action<KeyEventContext>` overloads |
| `Unbind(id)` / `Clear()` | Remove one or all bindings |
| `BoundCommands` | Currently bound `(Id, Pressed)` pairs |
| `Unbound` event | Fires for reported keys with no matching binding |

`KeyEventContext` carries `CommandId`, `Position`, `IsPressed` and `Action` (the decoded
theme action).

**Command IDs are page-agnostic.** The press event reports only `{row, col, pressed}` and
never identifies the page, so the same grid cell on two pages — or inside a folder — is
indistinguishable by position. The ID travels in the key's action descriptor, which is echoed
back on every press, so a binding keeps working when a button moves to another cell, page or
folder.

Two constraints follow from the protocol:

- **A key must have an action to be reported at all.** The device sends nothing for an
  unassigned key. `Command()` satisfies this while doing nothing on the device.
- **Device-native actions carry no command ID**, so they never match a binding.

Handlers run on the transport read thread: keep them short and queue slow work elsewhere. A
handler that throws is caught and logged, so it cannot stop the read loop or other bindings.

### 3.3 Encoder input

Encoder motion is reported through the same channel, with `Position.Row` and `Position.Column`
both set to a pseudo-row identifying the knob — `100` for the left, `103` for the right — and
`IsPressed` always `true` (encoders send no release). Use
`EncoderPositions.SideOfPseudoRow(row)` to map a reported row back to an `EncoderSide`.

A `Command()`-bound encoder reports the same value for clockwise, counter-clockwise and click,
so it identifies *which* knob moved, not which way. Bind `EncoderKeyboard(...)` when direction
matters — see [Physical rotary encoders](#physical-rotary-encoders).

---

## 4. Device operations

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
widget to in the theme is a valid key to push, including application-specific names. Most
integrations push their own known key set on a timer.

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

## 5. Reading a `.Theme` file

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
| `PageSwitchAction` | `pageSwitch` | `PageSwitchMode` (1=previous, 2=next, 0=absolute jump), `JumpToPage` (zero-based page index, used when mode is 0) |
| `OpenPageAction` | `openPage` | `PageName` (target page GUID) |
| `OneLevelUpAction` | `oneLevelUp` | `PageName` (always the sentinel `"parentPage"`, which resolves via the page's own `ParentPageName`) |
| `TextInputAction` | `text` | `InputText` (a command id when built with `KeyActions.Command`), `IsInputEnter`, `IsCopyPaste` |
| `EncoderKeyboardAction` | `encoder_keyboard` | `LeftKeycode`/`MiddleKeycode`/`RightKeycode` (+ labels), `Category` |
| `EncoderFunctionAction` | `encoder_system_volume`, `encoder_device_volume`, `encoder_device_brightness`, `encoder_system_media` | `Category`, `RelatedThemePath` |
| `UnknownKeyAction` | anything else | `RawFields` only |

Every `KeyAction` exposes `RawType`, `Description`, `ParentDescription`, `IconPath` and
`RawFields: IReadOnlyDictionary<string, TaggedValue>` for any field not promoted to a typed
property.

The vendor also ships `openWeb`, `qmk_mouse`, `Microphone`/`Loudspeaker` (volume),
`keyboard_switch` and `ControlFlow` keys. These are not modelled: they decode to
`UnknownKeyAction` with every field intact in `RawFields` and are re-encoded verbatim, so
loading and editing a vendor theme preserves them exactly. Use `KeyActions.Command(id)` to
implement equivalent host-side behaviour yourself.

---

## 6. Building a new theme

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
| `.AddEncoder(side, configure)` | A rotary encoder binding, positioned automatically (`EncoderSide.Left`/`Right`) — see [Physical rotary encoders](#physical-rotary-encoders). |
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
| `.Icon(fileName, bytes)` | Sets a static icon, normalised to the vendor format (128x128, RGB, no alpha). |
| `.IconPreservingAlpha(fileName, pngBytes)` | Same, but keeps the alpha channel so the background shows through — see [Transparent key icons](#transparent-key-icons). |
| `.AnimatedIcon(folderName, gifBytes)` | Sets a multi-frame animated icon (pressable key, unlike a decorative dynamic image). |
| `.IconAssetPath(path)` | Points at an already-registered/static system asset path (e.g. `/static/icon/dark/PageSwitch.png`) instead of registering a new one. |
| `.Action(keyAction)` | Assigns behavior — build one via `KeyActions` (below). |
| `.Title(text)` | On-screen label text over the icon. |
| `.Opacity(0-100)` | Icon transparency (100 = opaque, the default). |
| `.TitleStyle(fontFamily?, fontSize?, alignment?, color?)` | Overrides title font/colour; only `"top"`/`"bottom"` are confirmed real `alignment` values. |
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
KeyActions.Command("build.start")                                     // report an ID to YOUR C# handler
KeyActions.TypeText("hello", pressEnterAfter: true)                   // raw text action; nothing types it

// Encoders - see §6.4
KeyActions.EncoderKeyboard(leftKeycode, leftLabel, middleKeycode, middleLabel, rightKeycode, rightLabel)
KeyActions.EncoderKeyboard(rotateLeft: (KeyModifiers.LeftCtrl, HidKey.Z),
                           click: null,
                           rotateRight: (KeyModifiers.LeftCtrl, HidKey.Y))
KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume, relatedThemePath: null)
KeyActions.EncoderFunction("encoder_system_volume", relatedThemePath: null)   // raw-string overload
```

`HidKey` is an enum of the standard USB HID keyboard usage table (`A`-`Z`, `Digit0`-`Digit9`,
`Enter`, `Escape`, `Tab`, `Delete`, `F1`-`F12`, arrow keys, etc.) — use it instead of raw
integers. `KeyModifiers` is a `[Flags]` enum (`LeftCtrl`, `LeftShift`, `LeftAlt`, `LeftWin`,
`RightCtrl`, `RightShift`, `RightAlt`, `RightWin`); modifiers are packed into the upper byte
of the keycode, so `Ctrl+Shift+C` encodes as `0x0306`.

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
var media = builder.AddPage().SetCanvas(640, 656);    // page index 1
var window = builder.AddPage().SetCanvas(640, 656);   // page index 2

hub.AddKey(0, 0, key => key.Title("MEDIA").Action(KeyActions.JumpToPage(1)));
hub.AddKey(0, 1, key => key.Title("WINDOW").Action(KeyActions.JumpToPage(2)));

foreach (var section in new[] { media, window })
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
    .Title("TOOLS")
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
var tools = builder.AddPage().SetCanvas(640, 656).AsFolderOf(home);

// Fill the grid in reading order, skipping the reserved return cell.
var functions = new (string Label, HidKey Key)[]
{
    ("CUT",  HidKey.X), ("COPY",  HidKey.C), ("PASTE", HidKey.V),
    ("UNDO", HidKey.Z), ("REDO",  HidKey.Y),
};

int i = 0;
foreach (var (label, hidKey) in functions)
{
    int row = i / 5, col = i % 5;
    i++;
    tools.AddKey(row, col, key => key
        .Icon($"{label}.png", File.ReadAllBytes($"icons/{label}.png"))
        .Title(label)
        .Action(KeyActions.KeyboardCombo(KeyModifiers.LeftCtrl, hidKey)));
}

tools.AddKey(3, 4, key => key                          // reserved: return key
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

### Command IDs on keys

`KeyActions.Command(id)` stamps a caller-defined ID onto a key. The device executes nothing —
it reports the press with the ID, which your application routes to a handler via
`KeyBindings` ([§3.2](#32-running-your-own-code-on-a-button-press)).

```csharp
page.AddKey(0, 0, key => key
    .Icon("deploy.png", iconBytes)
    .Title("DEPLOY")
    .Action(KeyActions.Command("deploy.staging")));
```

`KeyActions.TypeText(text, pressEnterAfter, useCopyPaste)` is the underlying `text` action,
exposed so vendor themes round-trip with their `isInputEnter`/`isCopyPaste` flags intact.
Nothing types it. For real keystrokes use `Keyboard(...)` / `KeyboardCombo(...)`.

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

The MK20 has two rotary encoders. Each is an ordinary key placed at a fixed
secondary-screen coordinate, which is how the device recognises it — `AddEncoder` applies the
correct position for you.

| Member | Value |
|---|---|
| `EncoderPositions.LeftX` / `LeftY` | `106` / `0` |
| `EncoderPositions.RightX` / `RightY` | `320` / `0` |
| `EncoderPositions.LeftPseudoRow` / `RightPseudoRow` | `100` / `103` — the row/col reported on input |
| `EncoderPositions.SystemVolumeIcon`, `DeviceVolumeIcon`, `DeviceBrightnessIcon`, `SystemMediaIcon`, `KeyboardIcon` | Built-in icon asset paths |
| `EncoderPositions.PositionOf(side)`, `PseudoRowOf(side)`, `SideOfPseudoRow(row)` | Lookups |
| `EncoderPositions.RelatedThemePath(root, type)` | Builds the mini-display theme path (below) |

An encoder key is normally invisible — point it at a built-in icon and set `.Opacity(0)`. The
binding works regardless of what is drawn.

**Built-in device functions.** Executed entirely on-device, so they work with no software
running on the PC.

```csharp
page.AddEncoder(EncoderSide.Left, key => key
    .IconAssetPath(EncoderPositions.SystemVolumeIcon)
    .Opacity(0)
    .Action(KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)));
```

`EncoderFunctionType`: `SystemVolume` (the PC's volume), `DeviceVolume` (the device's own
speaker), `DeviceBrightness`, `SystemMedia`. A raw-string overload accepts any other
`encoder_*` type. The optional `relatedThemePath` names a mini-theme rendered on the
encoder's own small display:

```csharp
KeyActions.EncoderFunction(
    EncoderFunctionType.SystemVolume,
    EncoderPositions.RelatedThemePath(@"C:\...\ScreenKeyWindows_v1_1", EncoderFunctionType.SystemVolume));
```

**Keystrokes and combos per motion.** The only way to distinguish rotation direction; the
device sends a different keystroke for rotate-left, click and rotate-right. Pass `null` to
leave a motion unbound.

```csharp
page.AddEncoder(EncoderSide.Right, key => key
    .IconAssetPath(EncoderPositions.KeyboardIcon)
    .Opacity(0)
    .Action(KeyActions.EncoderKeyboard(
        rotateLeft:  (KeyModifiers.LeftCtrl, HidKey.Z),
        click:       (KeyModifiers.LeftCtrl | KeyModifiers.LeftShift, HidKey.C),
        rotateRight: (KeyModifiers.LeftCtrl, HidKey.Y))));
```

An `int`-based overload takes raw HID usages and labels directly:
`EncoderKeyboard(170, "Vol -", 168, "Mute", 169, "Vol +")`.

**Your own C#.** Routes the knob to a handler, but reports only *which* knob moved — clockwise,
counter-clockwise and click are indistinguishable (see [§3.3](#33-encoder-input)).

```csharp
page.AddEncoder(EncoderSide.Right, key => key.Action(KeyActions.Command("enc.right")));
```

A live readout of the current value can be placed near the encoder by binding a progress bar
or text item to `"Volume"` / `"device_bl"`. Use fully transparent colours
(`ThemeColor.Transparent`) to keep the function without showing anything:

```csharp
page.AddProgressBar(pb => pb.At(204, 96, 100, 12).BoundTo("Volume", 0, 100)
    .Colors(ThemeColor.Transparent, ThemeColor.Transparent, ThemeColor.Transparent));
```

### Transparent key icons

`Icon(...)` normalises to the vendor format — 128x128, RGB, no alpha — so transparent source
pixels are flattened onto black. `IconPreservingAlpha(...)` keeps the alpha channel, and the
device composites it against the screen background, so transparent areas reveal whatever is
behind the key including an animated background.

```csharp
page.AddKey(0, 0, key => key
    .IconPreservingAlpha("ring.png", File.ReadAllBytes("ring.png"))
    .Title("RING")
    .Action(KeyActions.Command("ring")));
```

Vendor themes never use alpha icons, so a theme built this way is device-only.

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
| Text | `.AddText(configure)` | 113 | `.At(x, y, z=1)`, `.Text(string)` or `.BoundTo(name)`, `.Font(descriptor, scale=1)`, `.Color(colour)` |
| Multiline text | `.AddMultilineText(configure)` | 116 | `.At(x, y, w=200, h=100, z=1)`, `.Text(...)`/`.BoundTo(name)`, `.Font(descriptor)`, `.Color(colour)` |
| Shadow text | `.AddShadowText(configure)` | 117 | `.At(x, y, z=1)`, `.Text(...)`/`.BoundTo(name)`, `.Font(descriptor)`, `.Color(colour)`, `.Border(colour, width=5)`, `.Shadow(colour, size=10)` |
| Digital clock field | `.AddDigitalClockField(configure)` | 111 | `.At(x, y, w=128, h=128, z=1)`, `.Field("hour"\|"minute"\|"second", displayDigits=2)`, `.Font(descriptor)`, `.Colors(front, back, border)` |

#### Colours

Every colour parameter takes a `ThemeColor`:

```csharp
new ThemeColor(0, 170, 255)             // opaque RGB
new ThemeColor(0, 170, 255, 220)        // with alpha
ThemeColor.White.WithAlpha(140)         // preset + alpha
ThemeColor.Transparent                  // hide a widget while keeping it functional
ThemeColor.Parse("#22D3EE")             // hex, with or without '#', optionally 8-digit
```

Components are range-checked at construction, so an out-of-range or malformed value fails
immediately rather than being written into a theme file. `ThemeColor.Black` and
`ThemeColor.White` are also provided, and `TryParse` gives a non-throwing parse.

A `string` converts implicitly, so a raw value copied out of an existing theme still works
wherever a colour is expected:

```csharp
ThemeColor colour = "r=0,g=170,b=255,a=220";
```

Font descriptors follow the confirmed real format
`"family,size,-1,5,weight,0,0,0,0,0[,style]"` (e.g. `"Microsoft YaHei,20,-1,5,50,0,0,0,0,0"`).

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
            .Colors(new ThemeColor(0, 170, 255, 220), ThemeColor.White.WithAlpha(140), ThemeColor.Black.WithAlpha(180)))
        .AddText(t => t.At(20, 55).BoundTo("CPU Usage").Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0"))
        .AddRadialGauge(rg => rg.At(300, 20, scale: 0.4).BoundTo("GPU Usage", 0, 100)
            .Gradient(new ThemeColor(0, 170, 255), new ThemeColor(255, 200, 0), new ThemeColor(255, 0, 0)))
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

## 7. Editing an existing theme

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

## 8. Uploading a theme to the device

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

## 9. Typical integration pattern

```csharp
// 1. Connect once at application startup.
var client = Mk20DeviceClient.CreateForSerialPort("COM7", loggerFactory: appLoggerFactory);
await client.ConnectAsync();

// 2. Build (or load+edit) a theme representing your app's button layout, upload once.
var theme = new ThemeBuilder()
    .AddPage(page => page
        .SetCanvas(640, 656)
        // A command id makes this key reachable from C# regardless of which page it sits on.
        .AddKey(0, 0, key => key.Icon("build.png", buildIconBytes)
            .Title("Build").Action(KeyActions.Command("build.start")))
        // A keystroke the device sends natively, so it also works when this app is closed.
        .AddKey(0, 1, key => key.Icon("save.png", saveIconBytes)
            .Title("Save").Action(KeyActions.KeyboardCombo(KeyModifiers.LeftCtrl, HidKey.S)))
        // The left knob adjusts the PC volume entirely on-device.
        .AddEncoder(EncoderSide.Left, key => key
            .IconAssetPath(EncoderPositions.SystemVolumeIcon).Opacity(0)
            .Action(KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)))
        // A gauge - the bound name is your own choice.
        .AddProgressBar(pb => pb.At(20, 20, 200, 30).BoundTo("CPU Usage", 0, 100)
            .Colors(new ThemeColor(0, 170, 255, 220), ThemeColor.White.WithAlpha(140), ThemeColor.Black.WithAlpha(180))))
    .Build();

await client.UploadThemeFileAsync("/data/theme/MK20/MyApp/MyApp.Theme", ThemeFileCodec.Encode(theme));

// 3. Bind your code to command ids.
var buttons = new KeyBindings(client);
buttons.OnCommand("build.start", StartBuild);

// 4. Periodically push live values your theme's gauges/text are bound to.
var timer = new System.Timers.Timer(500);
timer.Elapsed += async (_, _) => await client.PushSystemDataAsync(new Dictionary<string, string>
{
    ["CPU Usage"] = $"{GetCpuUsagePercent()}%",
});
timer.Start();

// 5. Clean up on shutdown.
timer.Dispose();
buttons.Dispose();
await client.DisconnectAsync();
await client.DisposeAsync();
```

To change a button's icon or function at runtime, decode the installed theme, edit it with
`ThemeEditor`, and re-upload — there is no live per-key command; every change requires
re-sending the whole file.

---

## 10. Error handling

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

## 11. Diagnostics

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
