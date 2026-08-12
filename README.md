# MK20 Control

A personal project for controlling and experimenting with the Waveshare MK10/MK20 host
protocol. The authoritative protocol specification is
[`PROTOCOL_WAVESHARE_MK20.md`](./PROTOCOL_WAVESHARE_MK20.md) - a datasheet-style reference
derived entirely from live USB capture of the vendor ScreenKeyWindows app and direct
interrogation of physical hardware (not from static disassembly or the official wiki's
guesses). This README covers build/usage instructions and additional narrative detail on
how each protocol fact was derived; the datasheet is the single source of truth for the
wire format itself.

This is **experimentation code**, not a polished production client. A few details (some
command IDs' payload schemas, achievable telemetry push rate) remain unconfirmed - see the
datasheet's "Open Items" section.

## Layout

```
MK20Control/
├── PROTOCOL_WAVESHARE_MK20.md      the protocol datasheet - authoritative wire-format spec
├── Mk20Control.sln
├── src/
│   ├── Mk20Control.Protocol/       reusable protocol library (packageable as a .dll)
│   │   ├── Checksums/Crc32.cs      zlib CRC-32 (frame payload integrity)
│   │   ├── Framing/                DeviceFrame + DeviceFrameHeader + DeviceFrameParser: the REAL wire framing
│   │   ├── Model/                  CommandId enum, KeyPosition, DeviceIdentity, ThemeListing, PacketType
│   │   ├── Codecs/                 VariantMapCodec (tagged-value format), SimpleStringMapCodec (untagged string map, FIND_DEVICE/GET_DEVICE_THEME/FILE_START/FILE_END), SystemDataCodec, ThemeFileCodec
│   │   ├── Theme/                  strongly-typed .Theme model: ThemeFile/ThemePage/ThemeCanvas/ThemeAsset
│   │   │   ├── Items/              ThemeItem hierarchy: Background/ProgressBar/Text/DynamicImage/Key/Unknown
│   │   │   └── Actions/            KeyAction hierarchy: Keyboard/OpenWeb/Mouse/PageSwitch/AudioVolume/TextInput/Unknown
│   │   ├── Transport/              ISerialTransport abstraction + SerialPortTransport implementation
│   │   ├── Client/                 Mk20DeviceClient - the main facade API (see below)
│   │   └── Exceptions/             Mk20ProtocolException + Timeout/UnconfirmedOperation/Checksum subtypes
│   └── Mk20Control.App/            interactive console sandbox built on Mk20DeviceClient
│       └── Program.cs              menu-driven scenarios (connect, ping, backlight, theme decode, ...)
├── tools/
│   ├── AssetGenerator/             generates the test assets below (re-run any time)
│   ├── CaptureAnalyzer/            decodes a Wireshark/USBPcap .pcapng capture of MK20 USB traffic
│   └── Captures/                   sample captures used for the findings below (see note)
└── assets/
    ├── icons/                      40 procedurally-generated 64x64 PNG icon badges
    └── backgrounds/                background/test-pattern images for the device canvas
```

> **`tools/Captures/`** holds the raw `.pcapng` files (capture.pcapng, capture2-14.pcapng)
> and their `*_decode_output.txt` text summaries used to derive every finding in this
> document. The `.pcapng` files are **sanitized**: filtered down to only the MK20's own
> USB traffic (by device address, matching VID `1d6b:0104`/`1234:5678`), stripping all
> other USB devices/hubs/Bluetooth activity that happened to be on the same bus during
> capture. They're excluded from version control via `.gitignore` (large binary files) -
> the `*_decode_output.txt` summaries are tracked and are the practical artifact to read;
> regenerate the `.pcapng`-derived output anytime with
> `dotnet run --project tools\CaptureAnalyzer -- tools\Captures\captureN.pcapng`.

## `Mk20Control.Protocol` - the reusable API surface

`Mk20Control.Protocol` is a plain class library (no console/UI dependencies) intended to
be packable as a standalone `.dll` and referenced from any other .NET project that wants
to talk to a real MK20. Everything recovered about the wire protocol and the `.Theme`
file format is exposed as strongly-typed classes/enums/records - callers never need to
touch raw byte arrays, dictionaries, or magic numbers. Logging is done throughout via
`Microsoft.Extensions.Logging.ILogger<T>` (defaults to a no-op logger if none is
supplied), so consumers can plug in their own logging provider.

The main entry point is `Mk20Control.Protocol.Client.Mk20DeviceClient`:

```csharp
using Microsoft.Extensions.Logging;
using Mk20Control.Protocol.Client;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
await using var client = Mk20DeviceClient.CreateForSerialPort("COM5", loggerFactory: loggerFactory);
await client.ConnectAsync();

var identity = await client.TryPingAsync();               // FIND_DEVICE - device model/screen/volume/backlight
await client.SetBacklightAsync(80);                        // SET_DEVICE_BL
await client.PushSystemDataAsync(new Dictionary<string, string> { ["CPU Usage"] = "42%" }); // SEND_SYSTEM_DATA_TO_DEVICE
var themes = await client.GetInstalledThemesAsync();        // GET_DEVICE_THEME - installed themes + free space
await client.ReloadThemeAsync("/data/theme/MK20/字母/字母.Theme"); // SET_DEVICE_RELOAD
await client.DeleteThemeAsync("/data/theme/MK20/字母/字母.Theme"); // SET_DEVICE_DELETE_THEME
byte[] themeBytes = ThemeFileCodec.Encode(myThemeFile);      // build a theme locally
await client.UploadThemeFileAsync("/data/theme/MK20/mytheme/mytheme.Theme", themeBytes); // FILE_START + bulk transfer + FILE_END + SET_DEVICE_RELOAD
await client.SendJsonAsync("{\"connect\":true}");           // SEND_JSON

client.NotificationReceived += (_, e) =>
    Console.WriteLine($"Key {e.Position} pressed={e.IsPressed}"); // DEVICE_ProactiveEscalationCMD events
```

Every public method's XML doc states plainly whether the operation is **CONFIRMED**
against real hardware captures or only ordering-inferred from firmware strings.
`SendRawCommandAsync` is an explicit escape hatch for unconfirmed `CommandId` values - it
logs a warning rather than silently pretending confidence it doesn't have.
`UploadThemeFileAsync` builds/edits a theme locally (via
`Mk20Control.Protocol.Codecs.ThemeFileCodec` and the `Mk20Control.Protocol.Theme` model)
and pushes it to the device end-to-end: `FILE_START` -> raw 4096-byte bulk chunks ->
`FILE_END` -> `SET_DEVICE_RELOAD` to activate it - **fully confirmed** by capturing a real
theme install and reconstructing the transferred bytes byte-for-byte from the capture (see
below).

### Building/editing themes without hand-writing JSON

`Mk20Control.Protocol.Theme.Building` provides a fluent API for exactly this - "set a
picture on button N and make it type X", "set the background", "add a CPU-usage gauge" -
without touching raw JSON:

```csharp
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Codecs;

var theme = new ThemeBuilder()
    .AddPage(page => page
        .SetCanvas(640, 656)
        .AddBackground(bg => bg.MainScreen("bg.png", File.ReadAllBytes("bg.png")))
        .AddKey(row: 0, column: 0, key => key
            .Icon("icon_01.png", File.ReadAllBytes("icon_01.png"))
            .Action(KeyActions.Keyboard(0x1E, "1"))) // USB HID keycode for '1'
        .AddKey(row: 0, column: 1, key => key
            .Icon("icon_02.png", File.ReadAllBytes("icon_02.png"))
            .Action(KeyActions.OpenWeb("https://example.com"))))
    .Build();

byte[] themeBytes = ThemeFileCodec.Encode(theme);
await client.UploadThemeFileAsync("/data/theme/MK20/mytheme/mytheme.Theme", themeBytes);
```

To edit an existing theme (e.g. one just downloaded from the device or loaded from disk)
rather than building from scratch, use `ThemeEditor`:

```csharp
var editor = new ThemeEditor(ThemeFileCodec.Decode(existingThemeBytes));
editor.Page(0).SetKeyIcon(row: 0, column: 2, "new_icon.png", File.ReadAllBytes("new_icon.png"));
editor.Page(0).SetKeyAction(row: 0, column: 2, KeyActions.TypeText("hello"));
byte[] updatedBytes = ThemeFileCodec.Encode(editor.Save());
```

`KeyActions` covers every confirmed action variant from the `.Theme` file format spec
(keyboard, URL, mouse, page navigation, typed text, audio volume, keyboard-layout switch,
encoder functions) - see PROTOCOL_WAVESHARE_MK20.md §7.3 for the full list and the
cross-check performed against real theme files (`CaptureAnalyzer --builder-byte-diff
<file.Theme>` decodes a real theme, rebuilds it purely through this API from the
interpreted data, and reports both a byte diff and a structured field-level comparison).

## Real wire format (confirmed via USBPcap capture)

Capturing the vendor **ScreenKeyWindows** app talking to a physical MK20 (USB device
VID:PID `1d6b:0104`, bulk endpoints `0x01` OUT / `0x81` IN, CDC-ACM) showed the actual
frame format is:

```
offset  size  field
0       22    ASCII literal "AA551234 FIXEDCMDHEAD " (with trailing space)
22      4     packetType (u32, little-endian)   0 = request (host->device), 2 = ack/reply (device->host)
26      4     cmd (u32, little-endian)          CMD_VALUE
30      4     payloadLen (u32, little-endian)
34      4     payloadCrc (u32, little-endian)   zlib crc32 of the payload
38      payloadLen   payload
```

There's also a separate literal ASCII control message observed on the wire:
`"AA551234 Abort file transfer 123455AA"` - this matches the doc's section 10.2
firmware-upgrade-framing guess almost exactly, except the real bytes are literal ASCII
text, not a raw `0xAA 0x55 0x12 0x34` binary magic.

**Confirmed `CMD_VALUE` numbers** (cross-checked against the doc's guessed ordering,
which turned out to match):
- `0` = `FIND_DEVICE` - zero-length ping/keepalive request; **CONFIRMED against real
  hardware** to also carry a non-empty identity reply (see below).
- `1` = `SEND_SYSTEM_DATA_TO_DEVICE` - payload is a length-prefixed serialized string
  key/value map (**big-endian** length prefixes/counts, unlike the little-endian
  frame header), e.g. `{"GPU Usage": "0%", "CPU Usage": "21%"}`
- `15` = `SEND_JSON` - UTF-8 JSON payload (getInfo-style replies, `deviceRequestSystemData`
  proactive-escalation messages, `{"connect": true}`, etc.)

Values `2`-`14` (`SET_DEVICE_RELOAD`, `GET_DEVICE_THEME`, `SET_DEVICE_BL`, `FILE_START`/
`FILE_END`, etc.) were also seen on the wire in the capture with binary/serialized
payloads, following the same enum ordering as the doc, but their internal payload schemas
aren't fully reverse-engineered yet (see `Mk20Control.Protocol.Model.CommandId` /
`CaptureAnalyzer`).

Additional confirmed details from decoding a full capture:
- `2` = `SET_DEVICE_RELOAD` - payload is a **plain UTF-8 path string, no length prefix**
  (unlike every other command's tagged/length-prefixed fields), e.g.
  `/data/theme/MK20/<theme name>/<theme name>.Theme`.
- `3` = `GET_DEVICE_THEME` - device reply lists installed themes as
  `(path, crc32)` pairs plus `bytesTotal`/`bytesAvailable` free-space fields.
- `6`/`7` = `FILE_START`/`FILE_END` - **CONFIRMED using `SimpleStringMapCodec`**: a
  single-entry string map, `{"<.Theme path>": "<size or crc32, as decimal text>"}`.
  **The bulk file transfer between them is also CONFIRMED** (capture 14, a real theme
  install): the raw file bytes are written directly to the bulk OUT endpoint in fixed
  4096-byte chunks (a shorter final remainder chunk), with no per-chunk framing or
  acknowledgment - verified by reconstructing the transferred bytes directly from the
  capture and finding them byte-for-byte identical to the source `.Theme` file, with a
  matching CRC-32. Implemented as `Mk20DeviceClient.UploadThemeFileAsync`.
- `11` = `SET_DEVICE_DELETE_THEME` - **CONFIRMED using `SimpleStringMapCodec`**
  (capture 13, "removed a theme from the device"): request `{"<.Theme path>": ""}`, reply
  `{"res": "1"}`. Previously only known from firmware string ordering.

### Two different map serializations - do not assume they're the same (confirmed against real hardware)

There are **two distinct, incompatible map encodings** in this protocol, and an earlier
version of this project's client code incorrectly assumed they were the same one - a bug
only caught by testing structured decoding against a physical device rather than the
capture files' looser best-effort string-scanning display:

1. **`VariantMapCodec`** (typeId-tagged: `typeId(u32 BE) + isNull(u8) + type-specific
   data`, supports int/double/bool/nested map/list/string/byte-array) - used by
   `DEVICE_ProactiveEscalationCMD` (cmd=13) events and inside `.Theme` files
   (header map + each key's `controlData`).
2. **`SimpleStringMapCodec`** (untagged: `count(u32 BE) + count * (string key + string
   value)`, where **every value is plain UTF-16BE text, even numeric-looking ones like a
   CRC-32 or a volume level**) - used by `FIND_DEVICE` (cmd=0) and `GET_DEVICE_THEME`
   (cmd=3) replies.

Confirmed by connecting directly to a physical MK20 over its serial port and dumping the
raw reply bytes: a `FIND_DEVICE` reply decodes as exactly
`{"version":"V2.32","upgradeToLatestMethod":"1","screen_width":"640","screen_model":"MK20",
"screen_height":"656","deviceVolume":"7","deviceName":"ScreenKey","deviceBl":"80"}` using
`SimpleStringMapCodec` - attempting to decode the same bytes with `VariantMapCodec` fails
with an "implausible string length" error, because what looks like a `typeId`/`isNull`
prefix is actually just the start of the (unprefixed-by-type) string value. The same is
true for `GET_DEVICE_THEME`'s reply (`bytesTotal`/`bytesAvailable`/theme-path-to-CRC
entries) - every value, including the CRC-32, is decimal text.

### What actually happens when you remap keys / change key pictures in ScreenKeyWindows

Capturing a session where three keys were remapped to A/B/C and two key pictures were
changed showed **no individual per-key protocol commands** (no `REQUEST_UPLOAD_KEY` /
`SEND_PIXMAP` calls were observed for this). Instead, the whole edited theme
(`时尚按键` / "Fashion Keys", reported as 2,547,288 bytes going up, CRC `105796399`) was
packaged by the app and sent as **one file upload**:

```
SET_DEVICE_RELOAD  "/data/theme/MK20/<theme>/<theme>.Theme"   (pre-check / unload?)
GET_DEVICE_THEME                                              (list themes + free space)
FILE_START  fileName=".../<theme>.Theme"  size=2547288
  ... bulk file transfer: raw bytes in 4096-byte chunks (confirmed, capture 14) ...
FILE_END    fileName=".../<theme>.Theme"  crc=105796399
SET_DEVICE_RELOAD  "/data/theme/MK20/<theme>/<theme>.Theme"   (activate the new theme)
```

**Implication:** key remapping and per-key image assignment are theme-editor-side
concepts baked into the `.Theme` file format itself, not live per-key wire commands.
To automate "set key N to image X" from custom host code, generate/edit a `.Theme` file
(`ThemeFileCodec`/`ThemeFile` model) and push it through this same file-upload + reload
sequence via `Mk20DeviceClient.UploadThemeFileAsync` - this is now fully implemented and
confirmed end-to-end. No live per-key commands (`REQUEST_UPLOAD_KEY`, `SEND_PIXMAP` as a
send path) have been observed, so per-key-only updates still require rebuilding the whole
theme file.

### Structured variant-map decoding (captures 2-5)

Four more captures were analyzed (brightness changes, adding a button-3 icon, assigning
"next/previous page" to buttons 20/16 and toggling them, assigning encoders to
volume/brightness, pressing all 20 buttons, and setting a GIF on button 4). All four
decode with **zero unresolved hex** using two additional confirmed pieces:

- **`SET_DEVICE_BL` (cmd=4) is CONFIRMED** (228 occurrences in one capture) - the payload
  is simply the **brightness level as ASCII decimal text** (`"99"`, `"100"`, no binary
  encoding, no length prefix).
- **`DEVICE_ProactiveEscalationCMD` (cmd=13) is CONFIRMED** as the device->host event for
  anything with rich descriptive metadata (page-switch keys, encoder assignment). Its
  payload is a structured serialized array of maps, fully reverse-engineered byte-by-byte
  and implemented in `Mk20Control.Protocol.Codecs.VariantMapCodec`:

  ```
  outer:       count(u32 BE) + count * variant map
  variant map: count(u32 BE) + count * (string key + tagged value)
  tagged value: typeId(u32 BE) + isNull(u8) + type-specific data
    typeId 2  = Int32 (BE)              typeId 9  = variant list
    typeId 6  = Double (BE)             typeId 10 = string (byteLen(u32 BE) + UTF-16BE)
    typeId 8  = nested variant map
  ```

  Example decoded event (button 20 pressed, assigned "Next page"):
  ```json
  [{"type":"keyState","row":3,"pressed":1,"col":4},
   {"type":"pageSwitch","parentDescription":"Page switching","pageSwitchMode":2,
    "jumpToPage":0,"iconPath":"/static/icon/dark/PageSwitch.png","description":"Page switching"}]
  ```
  Button 16 ("Previous page") showed `pageSwitchMode: 1` vs button 20's `pageSwitchMode: 2`.
  Encoder-assignment events use a sentinel `row: 104/105, col: 104/105` (not a real matrix
  position) with a descriptor like `{"type":"encoder_device_brightness","relatedTheme":
  "...device_brightness.Theme","category":"encoder", ...}`.

**Only keys/encoders with a currently-assigned "rich" action (page switch, encoder
function) produce `DEVICE_ProactiveEscalationCMD` events.** Pressing all 20 physical
keys in one capture produced escalation events for only 2 of them (the page-switch keys);
the other 18 had no assigned action in the loaded theme and produced **no wire traffic at
all** - there is no generic "any key was pressed" event; it's tied to the key having a
host-relevant action bound to it.

**Encoder rotation is not a discrete event.** Turning an encoder assigned to
brightness/volume does not send a "rotated by N" message. Instead, the host continuously
pushes the live current value via `SEND_SYSTEM_DATA_TO_DEVICE` (e.g. `device_bl=100`,
`Volume=78`), which an on-screen text/progress-bar control bound to that data-source key
renders. This was directly confirmed by diffing captures: with the volume/brightness
encoders on-screen, the device's `deviceRequestSystemData` list request included
`"Volume"` and `"device_bl"`; after removing the encoder widgets from the screen
(capture 3), the very same request reverted to the base 6-field list without them -
proving those two extra keys existed purely because of the on-screen encoder widgets.

**The `deviceRequestSystemData` handshake happens automatically on every theme
reload** (confirmed again via capture 10, which switched between several themes -
including a **secondary-screen** theme - and edited one live in the theme editor). Right
after a `SET_DEVICE_RELOAD` is acknowledged, the device proactively sends a `SEND_JSON`
(cmd=15) reply listing exactly which data-source keys the *newly loaded* theme's
progress-bar/text items are bound to, e.g.:
```json
{"deviceRequestSystemData":["CPU Usage","GPU Usage","RAM Usage"],
 "deviceRequestSystemDataShowUnit":true,"themePageSwitch":true}
```
The host is expected to push only those keys afterward - it's a declarative "what does
this theme need" contract, not a fixed/global telemetry set. Confirmed data-source key
names observed across all 10 captures so far: `"GPU Usage"`, `"GPU Temperature"`,
`"CPU Usage"`, `"CPU Temperature"`, `"RAM Usage"`, `"RAM Total Memory"`, `"Volume"`,
`"device_bl"`. **Secondary-screen themes use the exact same
`GET_DEVICE_THEME`/`FILE_START`/`FILE_END`/`SET_DEVICE_RELOAD` sequence and the same
`deviceRequestSystemData` handshake as main-screen themes** - there is no separate
protocol path for the secondary screen; it's just another `.Theme` file/slot.

**Setting a GIF on a key is also just a bigger theme file.** The theme size jumped from
~2.5-2.6 MB (PNG icons only) to **4,193,881 bytes** after assigning an animated GIF to a
key, uploaded via the same `FILE_START`/`FILE_END`/`SET_DEVICE_RELOAD` sequence - GIFs are
not a separate wire concept, just larger embedded theme-file payloads.

**Deleting a theme (capture 13) confirmed `SET_DEVICE_DELETE_THEME` (cmd=11)**, previously
only known from firmware string ordering. Removing a theme through the app's UI sent a
single request `{"<path>": ""}` (Simple String Map, path as the map key with an empty
value) and received `{"res":"1"}` back; a follow-up `GET_DEVICE_THEME` no longer listed
the deleted path. Exposed as `Mk20DeviceClient.DeleteThemeAsync`.

**Installing a new theme (capture 14) finally confirmed the missing bulk file-transfer
mechanism** between `FILE_START` and `FILE_END`. Earlier captures never showed it because
they only re-activated themes already present on the device; capture 14 recorded an actual
new-theme install. The transfer turned out to be almost disappointingly simple: after
`FILE_START` is acknowledged, the host writes the **raw `.Theme` file bytes directly to the
USB bulk OUT endpoint (0x01) in fixed 4096-byte chunks** (a shorter final remainder chunk
carries whatever's left over), back-to-back, with **no additional per-chunk header,
length-prefix, or acknowledgment of any kind** - it's exactly the file's bytes, split at
4096-byte boundaries. This was verified conclusively: reconstructing all 743,649 bytes
transferred for a `可爱按键.Theme` install directly from the capture produced a file
byte-for-byte identical to the original on disk, and its CRC-32 (`3131160337`) matched both
the source file's real CRC-32 and the value the device echoed back in the `FILE_END` reply.
`Mk20DeviceClient.UploadThemeFileAsync` now implements this full sequence end-to-end
(`FILE_START` -> chunked write -> `FILE_END` -> `SET_DEVICE_RELOAD`) instead of throwing.

For anything talking to a real MK20, use `Mk20Control.Protocol.Framing.DeviceFrame` /
`DeviceFrameParser` (or, for most callers, the `Mk20Control.Protocol.Client.Mk20DeviceClient`
facade built on top of them).

## The `.Theme` file format - fully reverse-engineered (captures 6-7 + static file analysis)

Beyond capturing the wire traffic, the actual on-disk `.Theme` files (found in
`ScreenKeyWindows_v1_1\theme\MK20\`) were byte-by-byte reverse engineered directly (not by
disassembling the vendor's Enigma-Protector-packed EXE - see note below). This is the
**complete answer to "how do I set keymaps / images / GIFs / mouse / sound actions on a
key"**, since all of it lives in this one file format, implemented in
`Mk20Control.Protocol.Codecs.ThemeFileCodec` (built on `VariantMapCodec`) and exposed both
as a strongly-typed `Mk20Control.Protocol.Theme.ThemeFile` model and via
`CaptureAnalyzer --theme <file.Theme>` for quick inspection:

```
[variant map: language(int), keyMacroValue(byte array), keyMacro(byte array, usually null)]
[8 bytes: 4 reserved zero bytes + 4 more bytes whose exact meaning is unclear/unreliable]
[UTF-8 JSON text: pages/canvas/items layout - found by scanning for balanced {}/[] while
 respecting quoted-string escaping, NOT by trusting any length prefix]
[1 reserved byte, observed as 0x0a]
[assetCount(u32 BE)]
repeat assetCount times:
    [pathByteLen(u32 BE)] [UTF-16BE path string, e.g. "/image/428x142/PhotoAlbum/xxx.gif"]
    [dataByteLen(u32 BE)] [raw asset bytes - PNG/GIF/MP4, confirmed via magic bytes]
```

The embedded JSON has a `"pages"` array; each page has a `"canvas"` (size/rotation/flip)
and an `"items"` array. **Confirmed by decoding all 18 real `.Theme` files shipped with
ScreenKeyWindows_v1_1** (retail themes, `defaultTheme.Theme`, the `Encoder\relatedTheme\*`
mini-themes, and all 6 `SecondaryScreen\*` themes) with zero unrecognized item types
remaining. Confirmed item `"type"` codes:

| type | Meaning | Key fields |
|---|---|---|
| `100` | Background image/video (main or secondary screen; **supports `.mp4`**) | `backgroundType`: `"main"`\|`"secondary"`, `path` |
| `102` | Progress bar (circular/linear) | `system_data_name`, `system_data_min_value`/`max_value`, colors |
| `103` | Linear bar gauge with solid front/back/border colors (`LinearGaugeItem`) | `system_data_name`, `front_color`/`back_color`/`border_color`/`border_width` |
| `109` | Radial/arc gauge with up to 3 gradient stops (`RadialGaugeItem`) | `system_data_name`, `angleMinValue`/`angleMaxValue`, `arcRadius`, `gradientColor1`-`3` |
| `111` | Live digital clock field (`DigitalClockItem`) - one item per field (hour/minute/second) | `system_data_name`: `"hour"`\|`"minute"`\|`"second"`, `text_font` |
| `113` | Text | `system_data_name` (when `system_data_flag`="1"), `text_font`, `text_str` |
| `114` | "Dynamic Image" - an **animated GIF** | `path` -> an embedded `.gif` asset, optional `system_data_name` |
| `115` | A **physical key** | `row`/`col` (matrix position), `path` -> its icon PNG, `controlData` (see below) |

A `type: 115` key item's `controlData` field is **base64 of another, separate,
independently-parseable variant map** (decode with `VariantMapCodec.DecodeMap`)
describing what the key actually *does*. The strongly-typed model represents this as a
`KeyAction` subclass; `UnknownKeyAction` remains the fallback for anything not yet modeled
(none was needed across all 18 real theme files as of this writing). **13 modeled action
types** confirmed so far (across captures 1-12 and all 18 real theme files):

```json
// Keyboard remap (the A/B/C example from capture 1) - keycode 4/5/6 = USB HID usage 'A'/'B'/'C'
{"type":"keyboard","keycode":4,"keyString":"A","description":"键盘"}

// Open a URL
{"type":"openWeb","Url":"www.google.com","parentDescription":"System file control","description":"Open the web page"}

// Mouse control (buttons/movement/scroll) - qmk_mouse_event selects click/move/scroll,
// qmk_mouse_key is the button, mouse_x/y/v/h are movement/scroll deltas
{"type":"qmk_mouse","qmk_mouse_key":0,"qmk_mouse_event":2,"mouse_x":0,"mouse_y":0,"mouse_v":0,"mouse_h":0}

// Page navigation (next/previous page) - pageSwitchMode 1=previous, 2=next (0 seen as a
// "current/no-op" state in a device->host echo)
{"type":"pageSwitch","pageSwitchMode":2,"jumpToPage":0,"description":"Page switching"}

// Jump directly to a specific page by id (distinct from relative pageSwitch above) -
// confirmed on defaultTheme.Theme's "create folder" keys (PageSwitchAction (KeyAction)
// modeled as OpenPageAction)
{"type":"openPage","pageName":"7473a82e-4164-4b4e-8a24-8a0f1afd4d22","description":"创建文件夹"}

// Navigate back up to the parent page - always pageName="parentPage" (a fixed sentinel, not a real page id)
{"type":"oneLevelUp","pageName":"parentPage","description":"返回到上一层"}

// Toggle/switch the active keyboard layout - no extra fields beyond the common base
{"type":"keyboard_switch","description":"键盘（切换）"}

// System volume - bound to a SPECIFIC named OS audio device
{"type":"Microphone","volumeAdjustMode":0,"volumeadjustValue":0,
 "volumeAdjustDevice":"Microphone (Logitech G733 Gaming Headset)","isSwitchDefaultDevice":false}
{"type":"Loudspeaker","volumeAdjustMode":0,"volumeadjustValue":0,
 "volumeAdjustDevice":"Speakers (Logitech G733 Gaming Headset)","isSwitchDefaultDevice":false}

// Typed text injection (capture 7: "added text input to a button")
{"type":"text","inputText":"text input hehehehehe","isInputEnter":false,"isCopyPaste":false,
 "description":"Text"}

// A "control flow" (multi-step macro) key - confirmed present but never actually configured
// with steps in any theme examined; controlDataList decoded to 4 zero bytes (an empty list
// header) - the schema for a POPULATED step list has not been observed, so
// ControlFlowAction only exposes the raw bytes rather than guessing at their structure
{"type":"ControlFlow","controlDataList":"AAAAAA==","description":"操作流"}

// Encoder function assignment (confirmed types: encoder_system_volume,
// encoder_system_media, encoder_device_brightness, encoder_keyboard) - the first three
// optionally bind to a "relatedTheme" .Theme file shown on the encoder's small display;
// encoder_keyboard instead binds a keycode to each of rotate-left/click/rotate-right
{"type":"encoder_device_brightness","relatedTheme":".../device_brightness.Theme","category":"encoder"}
{"type":"encoder_keyboard","encoder_left_keycode":170,"encoder_left_keyString":"Vol -",
 "encoder_middle_keycode":168,"encoder_middle_keyString":"Mute",
 "encoder_right_keycode":169,"encoder_right_keyString":"Vol +","category":"encoder"}
```

**Practical takeaway:** to programmatically assign any of the above to a key/page from
custom host code, you don't need any live per-key wire command - generate/edit the JSON +
asset section of a `.Theme` file matching this exact structure, then push it through the
confirmed `FILE_START` -> (bulk transfer) -> `FILE_END` -> `SET_DEVICE_RELOAD` sequence.
**There is no live command to set a single key's picture or function directly - it is
always done by rebuilding/editing the whole theme file.**

**A note on the Enigma-protected EXE.** `ScreenKeyWindows_v1_1.exe` is packed with
**Enigma Protector** (confirmed via its `.enigma1`/`.enigma2` PE sections - a commercial
anti-tamper/anti-disassembly tool). Static disassembly of it would show mostly
encrypted/virtualized stub code, not real logic, and attempting to unpack/bypass that
protection would mean circumventing the vendor's copy protection on a commercial
product - a different, unacceptable category from the black-box wire/file analysis used
throughout this project. Everything documented here was instead derived by directly
capturing and byte-analyzing real traffic and real `.Theme` files, the same legitimate
"clean room" method the original protocol doc itself used.

## CaptureAnalyzer - decode a Wireshark/USBPcap capture

```powershell
cd tools\CaptureAnalyzer
dotnet run -- "C:\path\to\capture.pcapng"
```

- Auto-detects the MK20's USB device address by matching known VIDs (`1d6b:0104` /
  `1234:5678`) via `tshark` (shells out to the Wireshark-bundled `tshark.exe`, default
  path `C:\Program Files\Wireshark\tshark.exe`, override with a 2nd argument).
- Pulls CDC-ACM bulk in/out payload bytes (`usbcom.data.in_payload` /
  `usbcom.data.out_payload` tshark fields), concatenates each direction, and feeds it
  through `DeviceFrameParser`.
- Prints every decoded frame: packet type, `CommandId` (with name), payload length, and a
  best-effort decode (JSON pretty-printed, `SEND_SYSTEM_DATA_TO_DEVICE` key/value pairs,
  `SEND_PIXMAP` JPEG detection, `DEVICE_ProactiveEscalationCMD` structured variant-map
  decoding, or a hex preview as a last resort).
- `--theme <file.Theme>` decodes an on-disk theme file directly (JSON layout + all
  embedded image/GIF/video assets) - no capture needed.
- `--theme-roundtrip <file.Theme>` decodes a theme, re-encodes it, decodes the result
  again, and compares structure/asset bytes end-to-end - useful for verifying
  `ThemeFileCodec` against a real file rather than only synthetic self-test data. All 18
  real `.Theme` files shipped with ScreenKeyWindows_v1_1 pass this check.
- `--builder-byte-diff <file.Theme>` decodes a real theme, rebuilds its Background+Key
  items purely through the `Mk20Control.Protocol.Theme.Building` API from the
  *interpreted* data (not by reusing each item's original raw JSON), re-encodes, and
  reports both a raw byte diff (expected to differ - see below) and a structured
  field-level comparison of every key's icon path and action. Confirms 0 mismatches
  across every physical (row/col-addressable) key in both `时尚按键.Theme` (39/39 keys)
  and `defaultTheme.Theme` (80/80 non-encoder-slot keys - encoder-function key entries
  share a fixed row=0,col=0 sentinel and aren't uniquely addressable by row/col, which
  this diagnostic script doesn't disambiguate; that's a script limitation, not a builder
  defect). Exact byte-for-byte equality is intentionally not required - only the
  confirmed-required JSON fields and their decoded meaning need to match; extra
  ScreenKeyWindows-only bookkeeping fields (`itemName`, `backupX`/`backupY`, JSON key
  ordering) are not reproduced by the builder and don't affect device behavior.
- `--selftest` verifies the frame/variant-map/system-data encode-decode round-trip against
  synthetic data, with no capture file needed.
- `--hex` prints the full wire bytes (`DeviceFrame.Encode()`) alongside each decoded frame -
  useful for extracting concrete byte-exact examples (see the protocol datasheet's §9).

Typical workflow: capture on the USBPcap interface while running ScreenKeyWindows and
doing something specific (remap a key, change a picture, load a theme), save as
`.pcapng`, then run this tool to see exactly what went over the wire.

## Running the live control app

```powershell
cd src\Mk20Control.App
dotnet run
```

`Mk20Control.App` is an interactive console sandbox built directly on
`Mk20DeviceClient` (the real, confirmed wire framing) with console logging enabled.
Menu options:
1. List serial ports (MK20 enumerates as USB CDC-ACM, VID:PID `1d6b:0104` or `1234:5678`)
2. Connect to device
3. Disconnect
4. Ping device (identity info via `FIND_DEVICE`)
5. Set backlight level (`SET_DEVICE_BL`)
6. Push sample telemetry (`SEND_SYSTEM_DATA_TO_DEVICE`)
7. Get installed themes (`GET_DEVICE_THEME`)
8. Reload a theme by device-side path (`SET_DEVICE_RELOAD`)
9. Listen for key/notification events (`DEVICE_ProactiveEscalationCMD`, Enter to stop)
10. Decode a local `.Theme` file and print its structure (no hardware needed)
11. Build a demo `.Theme` file locally (uses a generated icon), round-trip it through
    `ThemeFileCodec`, and save it to disk (no hardware needed)
12. Delete a theme from the device by device-side path (`SET_DEVICE_DELETE_THEME`)
13. Upload a local `.Theme` file to the device and activate it (`FILE_START` + bulk
    transfer + `FILE_END` + `SET_DEVICE_RELOAD`)
0. Exit

## Assets

### Icons (`assets/icons/icon_01.png` … `icon_40.png`)

40 placeholder icon badges at **64x64**, each a distinct color/shape/number, for testing
per-key icon assignment.

> Note: the device's actual key LCDs are documented at **128x128** px
> (wiki: "Button: 20 x 0.85" mechanical display keys, resolution 128×128"). 64x64 was
> generated per an earlier request; re-run `tools/AssetGenerator` with a different `size`
> if you want native-resolution icons - it's a one-line change (see `GenerateIcons` in
> `tools/AssetGenerator/Program.cs`).

### Backgrounds (`assets/backgrounds/`)

Four procedurally-generated designs (`gradient`, `grid_test`, `carbon_dark`, `color_bars`),
each rendered at the four device canvas sizes documented in the wiki and the protocol doc:

| Size name          | Resolution | Source |
|---|---|---|
| `full_canvas`      | 640×656 | protocol doc §6 (key grid 640×512 + ~144px secondary-screen band) |
| `main_screen`      | 640×512 | wiki: "Main screen: Overall area consisting of 20 keys" |
| `secondary_screen` | 428×142 | wiki: "Secondary screen: 2.8inch secondary screen" |
| `encoder`          | 214×142 | wiki: "Encoder: Encoder knob" |

`grid_test_*` includes a crosshair, 32px grid, and 4 distinct-colored corner markers -
useful for confirming orientation/cropping once an image is actually streamed to the
device.

Regenerate/redesign any time:

```powershell
cd tools\AssetGenerator
dotnet run -c Release
```

All artwork is procedurally generated code (gradients/shapes/text) - no third-party or
copyrighted images are used or bundled.

## Requirements

- .NET SDK 9.0+ (`dotnet --version` was 9.0.313 when this was set up)
- Windows (for the actual serial connection to the device; the code itself targets `net9.0`
  and doesn't use Windows-only APIs)
- [Wireshark](https://www.wireshark.org/) with USBPcap (for `CaptureAnalyzer`; `tshark.exe`
  ships alongside the main Wireshark install)
