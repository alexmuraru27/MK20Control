# Waveshare MK10 / MK20 — Host Protocol Reference

Reverse-engineered reference for building your own host software (e.g. a SimHub /
sim-racing telemetry bridge) that talks to a Waveshare **MK10** or **MK20** over USB —
**without modifying the device**.

> **Scope & legality.** Everything here concerns a *host-side* client speaking the
> device's existing USB serial protocol. That is interoperability with a documented,
> vendor-published protocol — you write 100% of your own code and redistribute none of
> theirs. Communication protocols/interfaces are not themselves copyrightable. Do **not**
> bundle Waveshare's proprietary daemon, their firmware image, or their app binaries in
> any derivative you distribute; ship your own code plus instructions. This document is
> engineering notes, not legal advice.

---

## Sources & provenance

Three binaries were mined. Confidence tags below map to these:

| Tag | Source | What it is |
|---|---|---|
| **[DEMO]** | `OpenSourceLicenseDemo_V1.0` (Qt/C++ source) | Waveshare's own reference host app — the "open" Layer-B protocol, fully readable source |
| **[FW]** | `MK20_V1.0.img` (848 MB Allwinner image) | The on-device Linux daemon; protocol strings & symbol names extracted |
| **[EXE]** | `ScreenKeyWindows_v1_1.exe` (Qt/C++, Inno installer) | The retail ScreenKey app — implements the full Layer-A protocol; Qt signal/slot table extracted |

**Confidence levels used throughout:**

- **VERIFIED** — byte-exact from readable source [DEMO], or cross-confirmed across ≥2 binaries.
- **CONFIRMED-PRESENT** — symbol/string exists in [FW]/[EXE]; semantics inferred from its name and Qt signal/slot signature.
- **INFERRED** — reasoned from geometry or convention; not directly observed.
- **UNVERIFIED** — plausible but untested; flagged for on-hardware confirmation.

---

## 1. Hardware & architecture

Two processors, one USB cable to the host. [FW][EXE]

```
        ┌─────────────────────────── MK20 ───────────────────────────┐
        │                                                             │
 USB-C  │   Allwinner T113-S3 (Linux)          GD32 MCU (QMK)         │
 ───────┼─▶  Qt daemon                          key matrix + knobs    │
 host   │    • owns all 21 displays              • "STM32" in code    │
        │    • files, audio, USB gadget    ◀──▶  (GD32 = STM32 clone) │
        │    • /dev/ttyGS0                  internal UART link         │
        │                                                             │
        │   20× key LCDs (128×128) + 1 secondary screen (428×142)     │
        └─────────────────────────────────────────────────────────────┘
```

- Input MCU is a **GD32** running QMK; the daemon's classes call it `STM32CommunicationObject` / `STM32CommunicationThread` (GD32 is an STM32-compatible clone). [FW]
- Key presses travel GD32 → T113 daemon → USB → host. [FW][EXE]
- The T113 can **reprogram the GD32 keymap** (`dynamic_keymap_set_keycode`) on host request. [FW][EXE]

### Transport [VERIFIED]

- **USB CDC-ACM** gadget. Device node on the T113: `/dev/ttyGS0`. [FW]
- Host sees a serial COM port with USB IDs **`1d6b:0104`** (Linux Foundation gadget) or **`1234:5678`**. [DEMO]
- The [DEMO] opens the port at 115200 8N1, no flow control. Over CDC-ACM the line-rate
  setting is typically a no-op (real speed = bulk endpoint), but this is **UNVERIFIED** —
  measure actual throughput on hardware before assuming.

---

## 2. Two protocol layers over one framing

The same wire framing carries two command vocabularies: [FW][EXE]

| Layer | Commands | Client | Model |
|---|---|---|---|
| **A — "Full ScreenKey"** | `FIND_DEVICE`, `GET_DEVICE_THEME`, `SEND_PIXMAP`, `SEND_SYSTEM_DATA_TO_DEVICE`, … | retail ScreenKey app | themes + pushed data sources |
| **B — "Open"** | `SHOW_JPG` (100), `JSON` (101) | the open-source demo | raw JPEG + JSON-RPC |

- **Layer B** is the simplest to implement and is fully documented in readable source.
- **Layer A** is richer and includes a **non-JPEG telemetry-push path** (§7) and **host-driven
  key remapping** (§8) — both directly useful for sim racing, at the cost of more complexity.

Version handshake exists: `device_protocolVersionV2_connect`; `deviceVersion = "V2.31"`. [FW][EXE]

---

## 3. Frame format [VERIFIED — byte-exact from DEMO]

From `packet_cmd_ack()` (`mainwindow.cpp`) and `CRC32.cpp`. **All integers little-endian.**

```
Offset  Size  Field           Notes
------  ----  --------------  ------------------------------------------------
  0      4    magic           A1 A5 5A 5E
  4      4    id (u32)        incrementing per frame; device error replies use id = 0xFFFFFFFF (-1)
  8      4    cmd (u32)       CMD_VALUE (100 = SHOW_JPG, 101 = JSON)
 12      4    size (u32)      payload length in bytes
 16      4    size_crc (u32)  crc32( the 4 size bytes at offset 12 )
 20    size   payload         JPEG (cmd 100) or compact JSON (cmd 101)
 20+s    4    data_crc (u32)  crc32( payload )
```

Minimum frame length = 24 bytes (magic + 4×u32 + trailing crc, zero payload).

### CRC-32 [VERIFIED]

Standard **zlib CRC-32**: reflected polynomial `0xEDB88320`, init `0xFFFFFFFF`, final XOR
`0xFFFFFFFF`. `CRC32.cpp` is the classic table implementation; Python `zlib.crc32(data) &
0xFFFFFFFF` matches byte-for-byte.

### Parsing notes [VERIFIED from DEMO]

- Resync by scanning for the 4-byte magic.
- `size_crc` lets you reject a corrupt length **before** trusting it. The vendor parser, on a
  bad `size_crc`, skips a whole 24-byte block — which can discard a valid frame that follows
  corruption. **Recommendation: on bad `size_crc`, resync to the *next* magic instead.**
- A frame is complete only when `buffer >= 20 + size + 4`.

---

## 4. Command & packet-type enums [CONFIRMED-PRESENT from FW+EXE]

### `CMD_VALUE` — full enum, in binary order (cross-confirmed FW & EXE)

```
Layer A (native ScreenKey):
   CMD_VALUE_FIND_DEVICE
   CMD_VALUE_SEND_SYSTEM_DATA_TO_DEVICE      push named data values to a theme
   CMD_VALUE_SET_DEVICE_RELOAD               load / reload a theme
   CMD_VALUE_GET_DEVICE_THEME
   CMD_VALUE_SET_DEVICE_BL                    backlight
   CMD_VALUE_SET_DEVICE_SCAN_STATE           key-scan enable
   CMD_VALUE_FILE_START
   CMD_VALUE_FILE_END
   CMD_VALUE_GET_DEVICE_VERSION
   CMD_VALUE_SET_DEVICE_CANVASFLIP           rotate/flip canvas
   CMD_VALUE_GET_DEVICE_SCREENMESSAGE
   CMD_VALUE_SET_DEVICE_DELETE_THEME
   CMD_VALUE_SEND_PIXMAP                      push a raw image region
   CMD_VALUE_DEVICE_ProactiveEscalationCMD   device→host request channel
   CMD_VALUE_REQUEST_UPLOAD_KEY              remap a key's QMK keycode
   CMD_VALUE_SEND_JSON                        generic JSON-RPC (Layer B rides this)

Layer B (open demo):
   CMD_VALUE_SHOW_JPG = 100                   [VERIFIED]
   CMD_VALUE_JSON     = 101                   [VERIFIED]
   CMD_VALUE_END      = 102                   [VERIFIED]
```

> **Exact numeric values for Layer-A commands are NOT pinned.** Order is confirmed across
> three binaries; the integers are not (needs disassembly, not string mining). Best inference:
> Layer A occupies a low contiguous range (≈0–15) and the open layer was appended at 100.
> **Verify against live bytes before hardcoding Layer-A command numbers.**

### `DATA_PACKET_TYPE` — outer classification [CONFIRMED-PRESENT FW+EXE]

The daemon dispatches on `processReceivedData(DATA_PACKET_TYPE, CMD_VALUE, QByteArray)`:

```
   DATA_PACKET_TYPE_CMD        a request
   DATA_PACKET_TYPE_FILE       bulk file payload
   DATA_PACKET_TYPE_CMD_ACK    reply to a request
   DATA_PACKET_TYPE_FILE_ACK   reply to a file chunk
```

The exact placement of the packet-type field in the Layer-A frame header is **UNVERIFIED**
(the Layer-B [DEMO] frame carries only `id`+`cmd`; Layer A may extend the header). Treat
`DATA_PACKET_TYPE` as a confirmed concept, not a confirmed byte offset.

---

## 5. Layer B — JSON-RPC (cmd 101) [VERIFIED from DEMO]

### Envelope

Request payload:
```json
{ "method": "<name>", "parameters": { ... } }
```
Reply payload:
```json
{ "ack_method": "<name>", "success": true,
  "errorString": "<message on failure>", "result": { ... } }
```
Unsolicited device event:
```json
{ "method": "<name>", "parameters": { ... } }
```
Device-side error: reply with `id = -1` and an `errorString`.

### Host → device methods

| Method | Parameters | Purpose |
|---|---|---|
| `getInfo` | — | model, canvas size, key geometry (§6) |
| `setBacklight` | `level` | screen brightness |
| `setVolume` | `level` (0–7) | audio volume |
| `playAudio` | `filePath` | play a WAV on-device (e.g. a shift buzzer) |
| `stopAudio` | — | stop playback |
| `keyboardInput` | `inputString` | **device emits these keystrokes to the host** |
| `getFilesBySuffix` | `suffixs` (array) | list files → `result.filePaths[] = {filePath, crc}` |
| `saveToFile` | `filePath`, `seek`, `data` (base64) | upload a file chunk |
| `setFileCRC` | `filePath`, `crc` | finalize an upload (integrity) |
| `deleteFiles` | `filePaths` (array) | delete on-device files |

### Device → host

| Message | Fields | Meaning |
|---|---|---|
| `keyStateChanged` | `col`, `row`, `pressed` | **key pressed/released** (also carries internal `keyCodeH/keyCodeL`) |
| any ack | `ack_method`, `success`, `result`, `errorString` | reply to a request |
| error | `id = -1`, `errorString` | device-side failure |

### File upload sequence [VERIFIED from DEMO]

```
for each 65536-byte chunk:
    → saveToFile { filePath, seek=<offset>, data=base64(chunk) }
    ← ack saveToFile { result.seek = <offset just written> }   # pull next chunk
when EOF:
    → setFileCRC { filePath, crc = zlib_crc32(whole file) }
```

---

## 6. Device info & canvas model [VERIFIED fields; values noted]

`getInfo` → `result`:

```json
{
  "deviceModel":  "<string>",
  "deviceVersion": "V2.31",
  "deviceWidth":  640,          // canvas width  — DEMO default; real value is authoritative
  "deviceHeight": 656,          // canvas height — DEMO default
  "screen_model": "<string>",
  "screen_width":  <int>,
  "screen_height": <int>,
  "devicePanel": {
    "rectCols": 5,
    "rectRows": 4,
    "rects": [
      { "x": .., "y": .., "width": .., "height": .., "col": .., "row": .., "isKey": true },
      ...   // one per key + the secondary-screen region
    ]
  }
}
```

### Canvas geometry [INFERRED arithmetic; number is DEMO default]

`rectwidget.cpp` hardcodes **640 × 656**, `rectCols=5`, `rectRows=4`:

```
width  640 = 5 cols × 128 px          (keys are 128×128, VERIFIED spec)
height 656 = (4 rows × 128) + 144
                512  key grid  +  ~144 secondary-screen band
                                   (secondary panel is 428×142; mapped into a 640×144 strip)
```

- **The device dictates the exact canvas size and rejects mismatches:** FW string
  *"Incorrect resolution, the device requires the resolution to be <X>"* (value formatted at
  runtime — not a constant). [FW]
- **JPEG has a max byte size:** FW string *"Image decoding failed, the image size may be
  larger than <X>"*. [FW]
- **Always trust `getInfo`'s `deviceWidth`/`deviceHeight` and `rects[]`**, not the 640×656
  literal. Draw on one canvas; the daemon slices per `rects[]`.

---

## 7. Layer A — image & data-source model [CONFIRMED-PRESENT from EXE]

Recovered from the app's Qt signal/slot table (`SerialPort` / `DeviceState` classes).

### 7.1 Commands (host → device slots)

| Slot / command | Payload | Effect |
|---|---|---|
| `device_set_system_data(_update)` → `SEND_SYSTEM_DATA_TO_DEVICE` | `QMap<QString,QString> system_data` | **push named data values** |
| `device_set_load` → `SET_DEVICE_RELOAD` | `reload_name` | load/reload a theme |
| `device_set_bl` → `SET_DEVICE_BL` | level | backlight |
| `device_set_canvas_flip` → `SET_DEVICE_CANVASFLIP` | `isCanvasFlip` | rotate/flip |
| `SEND_PIXMAP` | image region | push a raw pixmap (per-region, unlike Layer B's whole-canvas JPEG) |
| `request_upload_key_s` → `REQUEST_UPLOAD_KEY` | `{ type: uint8, keycode: uint16 }` | **remap a key** (§8) |
| `device_dispose_json` → `SEND_JSON` | `jsonObject` | generic JSON-RPC (= Layer B) |
| `device_set_serial_state` | `m_allow_device_open_serial_port` | gate serial |

### 7.2 States (device → host signals)

`device_state_device_online`, `_system_data`, `_reload`, `_bl`, `_theme`, `_version`,
`_canvas_flip`, `_screen_message`, `_receive_file`, `_delete_themes`,
`_send_pixmap_to_device`, plus:

- `device_keyState_Changed { pressed, row, col, keyCodeH, keyCodeL }` — the key event.
- `device_state_proactive_escalation_cmd (QList<QVariant>)` / `_message (object)` — the
  **device→host request channel** (`CMD_VALUE_DEVICE_ProactiveEscalationCMD`).
- `device_proactive_theme_load_finish (path)`.

### 7.3 ⭐ The non-JPEG telemetry path

The single most useful finding for sim racing. Data flows as a **string map**, not an image:

```
1. A THEME on the device declares fields bound to named data sources.
2. Device → host (proactive escalation):
       deviceRequestSystemData
       deviceRequestSystemDataShowUnit
       deviceRequestImageName
   naming the values it wants.
3. Host → device:  SEND_SYSTEM_DATA_TO_DEVICE  with  QMap<QString,QString>
       e.g. { "gear":"4", "speed":"212", "rpm":"8300", "delta":"-0.31" }
4. The on-device theme renders those values itself.
```

Built-in data sources in the retail app: **LibreHardwareMonitor, OpenWeather, audio**
(decibel/spectrum — hence a bundled FFTW), **gpu**, system time. [EXE]

**Implication:** numeric/text telemetry (gear, speed, RPM, fuel, delta) can be pushed as a
tiny string map and drawn on-device — **bypassing the whole-canvas JPEG frame-rate ceiling**
for exactly the fields a dash cares about. Cost: you must drive Layer A and author/load a
theme that declares those fields. The `system_data` **keys are arbitrary** — a custom theme
defines its own.

---

## 8. Key remapping & key actions [CONFIRMED-PRESENT from EXE]

### 8.1 Host-driven QMK remap

The app carries the **full QMK keycode name table** — media (Next/Rewind/FastForward/Play),
**Mouse Btn1–8**, F13–F24, consumer/system (Sleep/Wake/Calc/Mail), international keys, NKRO
toggle. Via `REQUEST_UPLOAD_KEY { type, keycode:uint16 }` → `dynamic_keymap_set_keycode` on
the GD32, **the host reprograms what each physical key emits.** This is your
button-configuration feature, and it is a first-class protocol operation.

> For **joystick** buttons specifically (conflict-free sim binding), bridge `keyStateChanged`
> events → **vJoy** on the PC side. Native remap covers keyboard/mouse/media/consumer codes,
> not gamepad buttons.

### 8.2 Host-executed key actions [CONFIRMED-PRESENT from EXE]

A key can trigger host-side actions; the vocabulary present in the app:

```
openWebUrl { url }         openFilePath { path }        keyMacro
setSystemVolume { volumeAdjustMode, value, isSwitchDefaultDevice }
setSystemInput  { inputText, isInputEnter, isCopyPaste }
OBS  { ToggleRecord, ToggleStream, SetCurrentProgramScene, GetRecordStatus, ... }
HomeAssistant { entity_id, GetState, homeAssistantControl }
xiaozhiAIData { method, parameters }   // on-device AI voice
```

---

## 9. Theme / page model [CONFIRMED-PRESENT from EXE/FW]

- Pages are **hierarchical**: `pageName` / `parentPageName`; navigate with
  `themePageSwitch` / `jumpToPage` / `openPage` / `oneLevelUp`.
- `isMultiState` keys (multi-state toggles), `keyMacro`, per-key `iconPath`.
- Per-theme stylesheets: light/dark × control/keyboard variants.
- A theme is the on-device UI definition that both the JPEG canvas and the data-source
  fields live inside. Themes are delivered via the file-transfer path (§10).

---

## 10. File transfer & firmware upgrade

### 10.1 Layer-A file transfer [CONFIRMED-PRESENT from EXE]

`FileSplitter`: `fileStart { filename, totalSize, fileData, crc32 }` streamed through
`FileState` { Prepare, Start, Sending, Stop, Finish, Error }, progress via
`host_send_file_state { progress }`, cancel via `setAbortFile`. Rides `DATA_PACKET_TYPE_FILE`
/ `_FILE_ACK`. (Layer B's simpler equivalent is `saveToFile`/`setFileCRC`, §5.)

### 10.2 Firmware upgrade — separate framing [CONFIRMED-PRESENT from FW]

**Not** the `A1A55A5E` app frame. Uses its own head/tail markers:

```
AA551234  FIXEDCMDHEAD  ...payload...  123455AA
AA551234  Abort file transfer          123455AA
```

Writes into `/data/DeviceUpgradeFirmware`, served over `/static/deviceResources`, progress
via `bytesTotal` / `bytesAvailable`. Rootfs is an **overlayfs** (`overlayfs:/overlay`).
*Not needed for a host client; documented for completeness.*

---

## 11. Integrations baked into the daemon [CONFIRMED-PRESENT from FW/EXE]

- **OBS Studio:** record/stream toggles, scene switching, scene-item enable, status queries.
- **Home Assistant:** `homeAssistantControl`, `homeAssistantStateChanged`, `entity_id`.
- **XiaoZhi AI:** voice, `emotion`, command control (needs the 2.4 GHz USB Wi-Fi dongle).
- **Encoder (knob) actions:** `encoder_qmk_mouse`, `encoder_keyboard`, `encoder_device_volume`,
  `encoder_device_brightness`, `encoder_system_volume`, `encoder_system_media`.
  > Knobs are handled as **configurable on-device actions**. No plain "knob rotated by N
  > detents" event was found in the host-facing API the way `keyStateChanged` exists for keys.
  > **Host-visible raw knob rotation is UNVERIFIED** — confirm on hardware.

---

## 12. Three telemetry routes for sim racing

| Route | Mechanism | Frame-rate ceiling | Effort | Layer |
|---|---|---|---|---|
| **B1 — JPEG mirror** | stream whole-canvas JPEG (`SHOW_JPG`) | bound by JPEG throughput (UNVERIFIED) | low | B |
| **A1 — data push** | push `system_data` string map to a custom theme | **not JPEG-bound** for text/number fields | medium | A |
| **A2 — hybrid** | data map for fast numbers + pixmap/JPEG for graphics | best of both | high | A+B |

All three are **pure-host and require no device modification** — A1/A2 simply speak the richer
protocol the stock app already uses, over the same wire.

- **B1** is the fastest to build and the natural first test (measure real fps).
- **A1** is the route most likely to give **shift-light-grade refresh** on the numbers that
  matter, with no on-device app — the data-source model was hiding in Layer A the whole time.

### Self-clocked frame loop [VERIFIED from DEMO]

For B1: after the device finishes rendering a `SHOW_JPG`, it **echoes a `SHOW_JPG` frame back**.
The host loop is **send → wait for echo → send**. That echo is built-in flow control **and** a
free, exact frame-rate meter.

---

## 13. Open questions to resolve on hardware

1. **Achievable frame rate (B1).** The single number that decides "responsive dash" vs
   "per-session icons". Measure via the self-clocked echo loop. **UNVERIFIED.**
2. **Does retail stock firmware speak this protocol,** or must you first flash Waveshare's
   official open-source image (via PhoenixCard)? If flashing is needed, note that installing a
   *vendor-provided* image with the *vendor's* tool is using the product as intended, not
   modifying it. **UNVERIFIED.**
3. **Exact Layer-A `CMD_VALUE` integers.** Order confirmed; numbers not. **UNVERIFIED.**
4. **Host-visible knob rotation.** Encoders act on-device; raw rotation to host not observed.
   **UNVERIFIED.**
5. **`DATA_PACKET_TYPE` byte placement** in the Layer-A header. **UNVERIFIED.**
6. **`system_data` request keys** for a given theme (custom themes define their own).

---

## 14. Quick reference — build a Layer-B client

```
CONNECT
  open COM port (USB 1d6b:0104 or 1234:5678)
  [optional] write ~1 MiB of '0' to flush the device parser   # DEMO does this
  → JSON getInfo
  ← ack getInfo → read deviceWidth, deviceHeight, devicePanel.rects[]

DISPLAY (B1)
  loop:
    render dash to a (deviceWidth × deviceHeight) RGB image
    jpeg = encode(image, quality)
    → frame(cmd=100, payload=jpeg)
    ← wait for cmd=100 echo    # render done; send next

INPUT
  on cmd=101 JSON with method "keyStateChanged":
    route {col,row,pressed} → vJoy / your logic

EXTRAS
  → JSON setBacklight { level }
  → JSON playAudio    { filePath }     # shift buzzer
  → JSON keyboardInput{ inputString }  # device types to host
```

Frame builder (pseudocode), all little-endian:

```
def frame(cmd, payload, id):
    size = len(payload)
    size_field = u32(size)
    return magic(A1A55A5E) + u32(id) + u32(cmd) + size_field
         + u32(zlib_crc32(size_field)) + payload + u32(zlib_crc32(payload))
```

---

*Compiled from static analysis of Waveshare's own open-source demo, the MK20 firmware image,
and the ScreenKey Windows application. No device was modified. Verify all UNVERIFIED items on
hardware before relying on them.*
