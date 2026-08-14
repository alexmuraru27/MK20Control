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
| 4 | **[SimRacingButtonBox](./04.SimRacingButtonBox)** | Multiple pages, folders, native keystrokes, and both encoders |
| 5 | **[EncodersAndArtwork](./05.EncodersAndArtwork)** | Animated backgrounds, transparent icons, every encoder binding style |

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

Most keys send keystrokes the device produces itself, so the bindings still work with this
program closed. Two keys use command ids for things a keystroke cannot express. The left
encoder sends a different keystroke per motion — the only way to distinguish clockwise from
counter-clockwise — while the right one uses a built-in device function.

### 5. EncodersAndArtwork

The presentation side: animated GIF backgrounds on both screens, and the same icons rendered
twice — once with their alpha channel preserved so the animation shows through the artwork,
once flattened — so the difference is visible side by side. Also includes an animated key
icon and both encoder binding styles.
