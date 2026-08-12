# MK20 Control

A personal project for controlling and experimenting with the Waveshare MK10/MK20 host
protocol, built from [`PROTOCOL_WAVESHARE_MK20.md`](./PROTOCOL_WAVESHARE_MK20.md) (a
reverse-engineered protocol reference), the official
[Waveshare MK20 wiki](https://www.waveshare.com/wiki/MK20), and a live USBPcap/Wireshark
capture of the real vendor ScreenKeyWindows app talking to physical hardware.

This is **experimentation code**, not a polished production client. The doc's guessed
`A1A55A5E` binary framing turned out to **not** match real hardware traffic - see
"Real wire format" below. Several other details (some Layer-A `CMD_VALUE` numbers,
achievable JPEG frame rate, knob-rotation events) are still not individually confirmed.

## Layout

```
MK20Control/
├── PROTOCOL_WAVESHARE_MK20.md      copy of the (partially superseded) protocol reference doc
├── Mk20Control.sln
├── src/
│   ├── Mk20Control.Protocol/       shared protocol library
│   │   ├── Crc32.cs                zlib CRC-32 (frame integrity)
│   │   ├── FrameCodec.cs           A1A55A5E framing as guessed in the doc (superseded, kept for reference)
│   │   ├── RealFrameCodec.cs       the REAL wire framing confirmed via USBPcap capture (see below)
│   │   ├── CmdValue.cs             CMD_VALUE / DATA_PACKET_TYPE constants (doc-guessed + capture-confirmed)
│   │   └── Models.cs               JSON-RPC envelope + getInfo/keyStateChanged DTOs (Layer-B/doc-only)
│   └── Mk20Control.App/            interactive console app (live serial control)
│       ├── Mk20Client.cs           SerialPort-based client (connect, getInfo, SHOW_JPG, ...)
│       └── Program.cs              menu-driven scenarios
├── tools/
│   ├── AssetGenerator/             generates the test assets below (re-run any time)
│   └── CaptureAnalyzer/            decodes a Wireshark/USBPcap .pcapng capture of MK20 USB traffic
└── assets/
    ├── icons/                      40 procedurally-generated 64x64 PNG icon badges
    └── backgrounds/                background/test-pattern images for the device canvas
```

## Real wire format (confirmed via USBPcap capture)

Capturing the vendor **ScreenKeyWindows** app talking to a physical MK20 (USB device
VID:PID `1d6b:0104`, bulk endpoints `0x01` OUT / `0x81` IN, CDC-ACM) showed the actual
frame format is **not** the `A1A55A5E` binary magic guessed in the protocol doc. It is:

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
- `0` = `FIND_DEVICE` - zero-length ping/keepalive observed
- `1` = `SEND_SYSTEM_DATA_TO_DEVICE` - payload is a Qt `QDataStream`-serialized
  `QMap<QString,QString>` (**big-endian** length prefixes/counts, unlike the little-endian
  frame header), e.g. `{"GPU Usage": "0%", "CPU Usage": "21%"}`
- `15` = `SEND_JSON` - UTF-8 JSON payload (getInfo-style replies, `deviceRequestSystemData`
  proactive-escalation messages, `{"connect": true}`, etc.)

Values `2`-`14` (`SET_DEVICE_RELOAD`, `GET_DEVICE_THEME`, `SET_DEVICE_BL`, `FILE_START`/
`FILE_END`, etc.) were also seen on the wire in the capture with binary/Qt-serialized
payloads, following the same enum ordering as the doc, but their internal payload schemas
aren't fully reverse-engineered yet (see `RealFrameCodec.cs` / `CaptureAnalyzer`).

Additional confirmed details from decoding a full capture:
- `2` = `SET_DEVICE_RELOAD` - payload is a **plain UTF-8 path string, no length prefix**
  (unlike every other command's Qt-QDataStream-style length-prefixed fields), e.g.
  `/data/theme/MK20/<theme name>/<theme name>.Theme`.
- `3` = `GET_DEVICE_THEME` - device reply lists installed themes as
  `(path, crc32)` pairs plus `bytesTotal`/`bytesAvailable` free-space fields.
- `6`/`7` = `FILE_START`/`FILE_END` - carry `fileName` (the `.Theme` path) and a size/CRC;
  the actual bulk theme file bytes were not captured in the traced session (likely a
  larger multi-chunk transfer on the same bulk endpoint, not yet isolated).

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
  ... (bulk file transfer, not captured in this session's trace) ...
FILE_END    fileName=".../<theme>.Theme"  crc=105796399
SET_DEVICE_RELOAD  "/data/theme/MK20/<theme>/<theme>.Theme"   (activate the new theme)
```

**Implication:** key remapping and per-key image assignment are theme-editor-side
concepts baked into the `.Theme` file format itself, not live per-key wire commands.
To automate "set key N to image X" from custom host code, you'd need to either (a)
generate/edit a `.Theme` file and push it through this same file-upload + reload
sequence, or (b) find the as-yet-unobserved live per-key commands (`REQUEST_UPLOAD_KEY`,
`SEND_PIXMAP`) by capturing a session that changes a single key without a full theme
save/reload.

This means: **anything in the app (`Mk20Control.App`) built against the old
`FrameCodec.cs`/`A1A55A5E` framing will not work against real hardware as-is.** Use
`RealFrameCodec.cs` (`RealFrameParser`/`RealFrame`) for anything talking to a real MK20.
The old code is kept for reference/comparison, not deleted, since it's exactly what the
doc describes and may still apply to the separate `OpenSourceLicenseDemo` app/protocol.

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
  through `RealFrameParser`.
- Prints every decoded frame: packet type, `CMD_VALUE` (with name), payload length, and a
  best-effort decode (JSON pretty-printed, `SEND_SYSTEM_DATA_TO_DEVICE` key/value pairs,
  JPEG detection, or a hex preview).
- `--selftest` verifies the encode/decode pipeline against synthetic data with no capture
  file needed.
- `--legacy-a1a55a5e` tries the old doc-guessed `A1A55A5E` framing instead, for comparison.

Typical workflow: capture on the USBPcap interface while running ScreenKeyWindows and
doing something specific (remap a key, change a picture, load a theme), save as
`.pcapng`, then run this tool to see exactly what went over the wire.

## Running the live control app

```powershell
cd src\Mk20Control.App
dotnet run
```

> **Note:** as of this writing, `Mk20Control.App`/`Mk20Client.cs` still targets the
> doc-guessed `A1A55A5E` framing (`FrameCodec.cs`), which the capture showed does **not**
> match real ScreenKeyWindows/MK20 traffic. Treat the live app as a starting point to be
> ported onto `RealFrameCodec.cs` next, not as something that currently works end-to-end
> against real hardware. `CaptureAnalyzer` (above) is the currently-working, capture-driven
> path for understanding real device behavior.

Menu options (once ported) let you:
1. List serial ports (MK20 enumerates as USB CDC-ACM, VID:PID `1d6b:0104` or `1234:5678`)
2. Connect (opens the COM port at 115200 8N1)
3. `getInfo` - print device model, canvas size, per-key rects
4. Set backlight / volume
5. Send a background from `assets/backgrounds/` as a full-canvas JPEG
6. Frame-rate test - loop-send a background and measure round-trip time
7. Push telemetry via `SEND_SYSTEM_DATA_TO_DEVICE`
8. Listen for key-press events
9. Play an on-device audio file

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
