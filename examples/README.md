# Examples

Five self-contained programs, in increasing order of complexity. Each one is a complete
project with its own assets — copy a folder somewhere else, change the project reference to
a NuGet/DLL reference, and it still runs.

Every example takes the serial port as its first argument, or reads `MK20_COM_PORT`:

```bash
dotnet run --project examples/01.HelloDevice -- COM7
```

```powershell
$env:MK20_COM_PORT = "COM7"
dotnet run --project examples/01.HelloDevice
```

> Close the vendor app before running any of these — it holds the serial port exclusively.

| # | Example | Shows |
|---|---|---|
| 1 | **[HelloDevice](./01.HelloDevice)** | Connect, identify the device, change the backlight, list installed themes |
| 2 | **[ButtonHandlers](./02.ButtonHandlers)** | Build a page, upload it, and run your own C# when a key is pressed |
| 3 | **[SystemMonitor](./03.SystemMonitor)** | Gauges and text bound to named channels, fed by a push loop |
| 4 | **[SimRacingButtonBox](./04.SimRacingButtonBox)** | Multiple pages, folders, logging every press by name, and both encoders |
| 5 | **[EncodersAndArtwork](./05.EncodersAndArtwork)** | Animated backgrounds, transparent icons, and encoders the device drives itself |

Looking for one specific piece rather than a whole program? See
[Building blocks](#building-blocks) at the bottom — every block, what it does, and which
example to read for it.

---

### 1. HelloDevice

No theme building at all — just enough to prove the wiring works. Reads the device identity,
dims and restores the backlight so you can see something happen, and lists what is installed.

Start here if a device is not responding.

### 2. ButtonHandlers

The core pattern for driving an application from the keypad. Each key carries a **command
id** you invent; the device reports the press with that id, and `KeyBindings` routes it to a
handler. One key is deliberately a plain keystroke instead, to show the difference: it keeps
working after the program exits.

### 3. SystemMonitor

Widgets never fetch their own data. Each is bound by name with `.BoundTo("cpu_usage")`, and
your program pushes a dictionary of those names on a timer with `PushSystemDataAsync`. The
names are yours to choose.

Values are sent as display strings (`"42%"`): the device shows the text and reads the leading
number for a gauge's fill level.

### 4. SimRacingButtonBox

The fullest example: two top-level pages plus three folders, showing all three navigation
mechanisms together — relative paging, `OpenPage` to enter a folder, and `OneLevelUp` to leave
one. A folder is simply a page that names its parent with `.AsFolderOf(...)`.

Every key uses a command id, so the device reports each press to this program and one shared
log prints which button it was — run it and press things to see the box narrate itself. That
also means the buttons do nothing on their own with this program closed; swap a key to
`KeyActions.Keyboard(...)` if you want it to work standalone, at the cost of the host no
longer seeing it at all. The left encoder does exactly that, sending a different keystroke
per motion — the only way to distinguish clockwise from counter-clockwise — while the right
one uses a built-in device function.

### 5. EncodersAndArtwork

The presentation side: animated GIF backgrounds on both screens, and the same icons rendered
twice — once with their alpha channel preserved so the animation shows through the artwork,
once flattened — so the difference is visible side by side. Also includes an animated key
icon, and gives both encoders a built-in device function so they work with nothing running.

---

## Building blocks

Every piece the examples use, what it does, and which example to read for a working use of
it. Everything below is covered in depth in
[`Mk20Control.Protocol.API.md`](../Mk20Control.Protocol.API.md).

**Talking to the device** — `Mk20DeviceClient`

| Block | What it does | In |
|---|---|---|
| `Mk20DeviceClient.CreateForSerialPort(port)` | Creates a client for a COM port. Dispose it to release the port | all |
| `ConnectAsync()` | Opens the link. Nothing else works before this | all |
| `TryPingAsync()` | Asks the device to identify itself — the quickest "is it alive?" check | 1 |
| `GetInstalledThemesAsync()` | Lists the themes already on the device | 1 |
| `SetBacklightAsync(percent)` | Sets screen brightness, 0–100 | 1 |
| `UploadThemeFileAsync(path, bytes, timeout)` | Sends a theme and activates it. The one call that puts your layout on screen | 2–5 |
| `PushSystemDataAsync(dictionary)` | Pushes named values for widgets to display. The device never fetches data itself | 3, 4 |
| `PageSwitched` | Fires when the active page changes — useful while laying a box out | 4 |

**Building a theme** — `ThemeBuilder`

| Block | What it does | In |
|---|---|---|
| `new ThemeBuilder()` … `.Build()` | Collects pages, then produces the `ThemeFile` | 2–5 |
| `ThemeFileCodec.Encode(theme)` | Turns that `ThemeFile` into the bytes you upload | 2–5 |
| `builder.AddPage().SetCanvas(640, 656)` | Adds a page. `640×656` is the full device canvas | 2–5 |
| `.AsFolderOf(parentPage)` | Marks a page as a folder of another — that is all a "folder" is | 4 |
| `page.AddKey(row, col, …)` | Places a key on the 5×4 grid | 2–5 |
| `page.AddEncoder(EncoderSide.Left \| .Right, …)` | Configures one of the two rotary encoders | 4, 5 |

**How a key looks**

| Block | What it does | In |
|---|---|---|
| `.Title("BOOST")` | The text drawn on the key. Independent of the action | 2–5 |
| `.IconAssetPath(bytes)` | Draws an image on the key; PNG alpha is preserved | 4, 5 |
| `.Opacity(0)` | Hides the default key background so a transparent icon shows the page behind it | 4, 5 |

**What a press does** — `KeyActions`

| Block | What it does | In |
|---|---|---|
| `Command("racing.limiter")` | Reports the press to your program. The only way the host learns a key was pressed | 2, 3, 4 |
| `Keyboard(key)` / `KeyboardCombo(mods, key)` | The device types a real keystroke itself. Works with your program closed, but the host never sees it | 5 / 2, 5 |
| `OpenPage(id)` / `OneLevelUp()` | Enters a folder / leaves one. Performed by the device | 4 |
| `PreviousPage()` / `NextPage()` | Steps between top-level pages. Performed by the device | 4 |
| `EncoderKeyboard(rotateLeft, click, rotateRight)` | A different keystroke per motion — the only way to tell the two directions apart | 4 |
| `EncoderFunction(EncoderFunctionType.SystemVolume)` | A built-in function the device performs alone, e.g. volume or brightness | 4, 5 |

> Pick per key: an action either notifies the host **or** produces a keystroke, never both.

**Screen elements**

| Block | What it does | In |
|---|---|---|
| `page.AddText(…)` | Static or data-bound text | 3 |
| `page.AddProgressBar(…)` | A horizontal bar that fills to a value | 3 |
| `page.AddRadialGauge(…)` | A circular dial. Rendered at `radius × 2`, anchored top-left | 3 |
| `page.AddDigitalClockField(…)` | One clock digit group — add `hour`, `minute` and `second` for a full clock | 4 |
| `page.AddDynamicImage(…)` | An animated GIF, including full-screen backgrounds | 5 |
| `.At(x, y, w, h)` | Position and size on the canvas | 3, 4 |
| `.BoundTo("cpu_usage", 0, 100)` | Binds the element to a name you push with `PushSystemDataAsync` | 3 |
| `.MainScreenBackgroundAutoFit(…)` / `.SecondaryScreenBackgroundAutoFit(…)` | Scales an image to fill a whole screen | 5 |

**Reacting to presses** — `KeyBindings`

| Block | What it does | In |
|---|---|---|
| `new KeyBindings(client)` | Routes incoming key events. Dispose it to stop listening | 2, 4 |
| `OnCommand(id, handler)` | Runs your C# when the key with that id is pressed | 2, 4 |
| `OnCommandRelease(id, handler)` | The same, on release — for hold-style controls | 2 |
| `Unbound` | Catches every press without its own handler. Ideal for one shared log | 2, 4 |

Two facts worth knowing before you design a layout: a press reports only `{row, column,
pressed}` and never says which page it came from, so the **command id is what identifies a
button** — which is why it must be unique, and why a binding keeps working when you move a
button to another cell, page or folder. And widgets, including the clock, are **fed entirely
by the host**; stop pushing and the display freezes at the last value it received.
