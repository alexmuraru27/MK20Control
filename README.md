<div align="center">

# MK20 Control

**A .NET library for the Waveshare MK20 macro keypad — reverse-engineered from the wire up.**

Build themes, drive both screens, and run your own C# when a key is pressed.
No vendor software required.

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](./LICENSE)
[![Tests](https://img.shields.io/badge/tests-88%20passing-brightgreen)](#testing)
[![Status](https://img.shields.io/badge/status-experimental-orange)](#project-status)

[Quick start](#quick-start) · [Features](#features) · [Documentation](#documentation) · [Testing](#testing)

</div>

---

## What is this?

The Waveshare MK20 is a 20-key macro keypad with an LCD under every key, a secondary
display, and two rotary encoders. It ships with a Windows app that talks to it over an
undocumented USB protocol.

This project documents that protocol and implements it as a reusable .NET library, so any
application can drive the device directly.

```
┌───────────────────────────────────────┐
│ (O)     secondary 428 × 142     (O)   │   2 rotary encoders
├───────────────────────────────────────┤
│   ▣   ▣   ▣   ▣   ▣    main screen    │   20 keys · 4 × 5 grid
│   ▣   ▣   ▣   ▣   ▣    640 × 512      │   each key is a 128 × 128 LCD
│   ▣   ▣   ▣   ▣   ▣                   │
│   ▣   ▣   ▣   ▣   ▣                   │
└───────────────────────────────────────┘
      one 640 × 656 canvas, top band first
```

Everything here was derived from live USB captures of the vendor app and direct
interrogation of real hardware — not from disassembly or vendor documentation. Facts are
labelled **confirmed** only when reproduced against a physical device.

## Quick start

```bash
git clone <this-repo> && cd MK20Control
dotnet build
```

Reference `src/Mk20Control.Protocol`, then:

```csharp
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Host;
using Mk20Control.Protocol.Theme.Building;

await using var client = Mk20DeviceClient.CreateForSerialPort("COM7");
await client.ConnectAsync();

// Design a page: an icon, a label, and an id you choose.
var theme = new ThemeBuilder()
    .AddPage(page => page
        .SetCanvas(640, 656)
        .AddKey(0, 0, key => key
            .Icon("build.png", File.ReadAllBytes("build.png"))
            .Title("Build")
            .Action(KeyActions.Command("build.start"))))
    .Build();

await client.UploadThemeFileAsync(
    "/data/theme/MK20/MyApp/MyApp.Theme", ThemeFileCodec.Encode(theme));

// Run your own code when that key is pressed.
using var buttons = new KeyBindings(client);
buttons.OnCommand("build.start", () => StartBuild());
```

That's the whole loop: describe a layout, upload it, bind handlers.

## Features

| | |
|---|---|
| **Theme authoring** | Fluent builder for pages, keys, icons and titles — no hand-written JSON |
| **Folders & paging** | Relative paging, absolute jumps, and arbitrarily nested folders |
| **Both screens** | Video, image or animated GIF backgrounds on the main *and* secondary display |
| **Widgets** | Progress bars, linear/radial/circular gauges, text, and clock fields, all data-bound |
| **Key actions** | Native keystrokes and modifier combos the device sends itself — they work with your app closed |
| **Encoders** | Volume, brightness and media built-ins, or a different keystroke per rotate/click |
| **Your own C#** | Give a key an id, bind a handler — page-agnostic, so moving the key doesn't break it |
| **Editing** | Decode any existing theme, change one key, re-encode — untouched data survives verbatim |
| **Telemetry** | Push live values the device renders in its widgets |
| **Diagnostics** | Wire-level logging and a `.pcapng` capture analyzer |

### Transparent icons

Key icons normally have no alpha channel. `IconPreservingAlpha(...)` keeps it, and the
device composites it against the screen background — so a transparent icon shows the
animated background through the artwork. **No vendor theme can do this.**

## Documentation

| Document | Read it for |
|---|---|
| **[API Reference](./Mk20Control.Protocol.API.md)** | Using the library: every public type, with runnable examples |
| **[Protocol Datasheet](./PROTOCOL_WAVESHARE_MK20.md)** | The wire protocol and `.Theme` file format, independent of any implementation |

## Repository layout

```
src/
  Mk20Control.Protocol/          the library
    Client/                      Mk20DeviceClient - connect, upload, control, receive events
    Host/                        KeyBindings - route key presses to your code
    Theme/Building/              fluent builders (ThemeBuilder, KeyActions, ThemeColor, ...)
    Theme/Items/                 decoded theme model (keys, backgrounds, widgets)
    Codecs/ Framing/ Transport/  wire format, framing and serial transport
  Mk20Control.IntegrationTests/  NUnit tests - offline (always run) + hardware (opt-in)
tools/
  AssetGenerator/                generates the test icons and backgrounds
  CaptureAnalyzer/               decodes a USB capture, or a .Theme file, from the CLI
assets/                          icons, backgrounds and GIFs used by the tests
```

## Testing

```bash
dotnet test src/Mk20Control.IntegrationTests
```

Tests come in two flavours:

- **Offline** — build a theme, decode it back, assert the round-trip. No hardware; these
  always run.
- **Hardware** — connect to a real device and produce a visible effect. Set `MK20_COM_PORT`
  (e.g. `COM7`) to enable them; without it they're **skipped, not failed**.

```bash
$env:MK20_COM_PORT = "COM7"      # PowerShell
dotnet test src/Mk20Control.IntegrationTests
```

> Close the vendor app first — it holds the serial port exclusively.

There is also a self-test suite that needs no device and no test runner:

```bash
dotnet run --project tools/CaptureAnalyzer -- --selftest
```

### Tools

```bash
# Decode a .Theme file, or a USB capture
dotnet run --project tools/CaptureAnalyzer -- --theme path/to/file.Theme
dotnet run --project tools/CaptureAnalyzer -- capture.pcapng

# Regenerate the test icons/backgrounds in assets/
dotnet run --project tools/AssetGenerator
```

`tools/Captures/` holds the USB captures behind the protocol findings, alongside
`*_decode_output.txt` summaries that are readable without Wireshark. `sanitize.ps1` filters
a raw capture down to just the MK20's own traffic.

## Project status

Working and verified against real hardware, but **experimental** — expect rough edges, and
read the datasheet's *Open Items* before depending on anything subtle.

Three constraints are inherited from the device itself:

- **No live per-key updates.** Changing one button means re-uploading the whole theme; the
  protocol has no per-key command.
- **Encoders don't report rotation direction** to managed code. Bind keystrokes per motion
  when direction matters.
- **A stuck upload can wedge the device.** An unacknowledged `FILE_END` may require a
  physical replug; the client fails fast rather than retrying into a dead link.

## Requirements

- .NET 9 SDK
- Windows for the serial transport (`System.IO.Ports`) and hardware tests
- A Waveshare MK20 — only for the hardware tests; everything else runs offline

## License

[GPL-3.0](./LICENSE).

Not affiliated with or endorsed by Waveshare. "MK20" and "ScreenKeyWindows" belong to their
respective owners. This is an independent, clean-room reimplementation based on observing
traffic to hardware the author owns.
