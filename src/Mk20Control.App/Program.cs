using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using Mk20Control.App;
using Mk20Control.Protocol;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Repo layout: Mk20Control\assets\{icons,backgrounds}, this project at Mk20Control\src\Mk20Control.App.
string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string assetsDir = Path.Combine(repoRoot, "assets");
string iconsDir = Path.Combine(assetsDir, "icons");
string backgroundsDir = Path.Combine(assetsDir, "backgrounds");

Console.WriteLine("=== MK20 Sandbox ===");
Console.WriteLine("Reference: ../../../PROTOCOL_WAVESHARE_MK20.md");
Console.WriteLine($"Assets: {assetsDir}");
Console.WriteLine();

Mk20Client? client = null;

while (true)
{
    Console.WriteLine();
    Console.WriteLine("1) List serial ports");
    Console.WriteLine("2) Connect to device");
    Console.WriteLine("3) getInfo (device model / canvas / key rects)");
    Console.WriteLine("4) Set backlight level");
    Console.WriteLine("5) Set volume level");
    Console.WriteLine("6) Send a background image as full-canvas JPEG (B1 / SHOW_JPG)");
    Console.WriteLine("7) Frame-rate test: loop-send a background, measure echo fps");
    Console.WriteLine("8) Push fake telemetry via system_data (A1 route, best-effort)");
    Console.WriteLine("9) Listen for key presses (Ctrl+C to stop)");
    Console.WriteLine("10) Play an audio file on-device");
    Console.WriteLine("0) Exit");
    Console.Write("> ");
    var choice = Console.ReadLine();

    try
    {
        switch (choice)
        {
            case "1":
                ListPorts();
                break;
            case "2":
                client = await ConnectAsync();
                break;
            case "3":
                await GetInfoAsync(Require(client));
                break;
            case "4":
                await SetBacklightAsync(Require(client));
                break;
            case "5":
                await SetVolumeAsync(Require(client));
                break;
            case "6":
                await SendBackgroundAsync(Require(client), backgroundsDir);
                break;
            case "7":
                await FrameRateTestAsync(Require(client), backgroundsDir);
                break;
            case "8":
                await PushFakeTelemetryAsync(Require(client));
                break;
            case "9":
                ListenForKeys(Require(client));
                break;
            case "10":
                await PlayAudioAsync(Require(client));
                break;
            case "0":
                client?.Dispose();
                return;
            default:
                Console.WriteLine("Unknown choice.");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[error] {ex.GetType().Name}: {ex.Message}");
    }
}

static Mk20Client Require(Mk20Client? client) =>
    client ?? throw new InvalidOperationException("Connect to the device first (option 2).");

static void ListPorts()
{
    var ports = SerialPort.GetPortNames();
    if (ports.Length == 0)
    {
        Console.WriteLine("No serial ports found.");
        return;
    }
    Console.WriteLine("Available ports: " + string.Join(", ", ports));
    Console.WriteLine("MK20 typically enumerates as a USB CDC-ACM device (USB VID:PID 1d6b:0104 or 1234:5678).");
}

static Task<Mk20Client> ConnectAsync()
{
    Console.Write("COM port (e.g. COM5): ");
    string port = Console.ReadLine()?.Trim() ?? "";
    if (string.IsNullOrWhiteSpace(port))
        throw new InvalidOperationException("No port entered.");

    var client = new Mk20Client(port);
    client.Log += msg => Console.WriteLine($"[log] {msg}");
    client.KeyStateChanged += evt =>
        Console.WriteLine($"[key] col={evt.Col} row={evt.Row} pressed={evt.Pressed}");
    client.Open();
    return Task.FromResult(client);
}

static async Task GetInfoAsync(Mk20Client client)
{
    var info = await client.GetInfoAsync();
    Console.WriteLine($"model={info.DeviceModel} version={info.DeviceVersion}");
    Console.WriteLine($"canvas={info.DeviceWidth}x{info.DeviceHeight} screen={info.ScreenModel} {info.ScreenWidth}x{info.ScreenHeight}");
    if (info.DevicePanel is { } panel)
    {
        Console.WriteLine($"panel cols={panel.RectCols} rows={panel.RectRows} rects={panel.Rects?.Count ?? 0}");
        foreach (var r in panel.Rects ?? new List<DeviceRect>())
        {
            Console.WriteLine($"  rect col={r.Col} row={r.Row} x={r.X} y={r.Y} w={r.Width} h={r.Height} isKey={r.IsKey}");
        }
    }
}

static async Task SetBacklightAsync(Mk20Client client)
{
    Console.Write("Backlight level (0-100): ");
    int level = int.Parse(Console.ReadLine() ?? "50");
    var reply = await client.SetBacklightAsync(level);
    Console.WriteLine($"success={reply.Success} error={reply.ErrorString}");
}

static async Task SetVolumeAsync(Mk20Client client)
{
    Console.Write("Volume level (0-7): ");
    int level = int.Parse(Console.ReadLine() ?? "4");
    var reply = await client.SetVolumeAsync(level);
    Console.WriteLine($"success={reply.Success} error={reply.ErrorString}");
}

static string PickBackground(string backgroundsDir)
{
    var files = Directory.GetFiles(backgroundsDir, "*.png")
        .Concat(Directory.GetFiles(backgroundsDir, "*.jpg"))
        .OrderBy(f => f)
        .ToList();
    if (files.Count == 0)
        throw new InvalidOperationException($"No images found in {backgroundsDir}. Run the AssetGenerator tool first.");

    Console.WriteLine("Available backgrounds:");
    for (int i = 0; i < files.Count; i++)
        Console.WriteLine($"  {i}: {Path.GetFileName(files[i])}");
    Console.Write("Pick index: ");
    int idx = int.Parse(Console.ReadLine() ?? "0");
    return files[idx];
}

/// <summary>
/// Fits/crops the source image onto a canvas of exactly the requested size and encodes it as JPEG.
/// Per the doc, the device dictates the exact canvas size (getInfo's deviceWidth/deviceHeight)
/// and rejects mismatches, so we always resize to that.
/// </summary>
static byte[] EncodeJpegForCanvas(string imagePath, int width, int height, int quality = 85)
{
    using var img = Image.Load<Rgb24>(imagePath);
    img.Mutate(x => x.Resize(new ResizeOptions
    {
        Size = new Size(width, height),
        Mode = ResizeMode.Crop,
    }));
    using var ms = new MemoryStream();
    img.Save(ms, new JpegEncoder { Quality = quality });
    return ms.ToArray();
}

static async Task SendBackgroundAsync(Mk20Client client, string backgroundsDir)
{
    var info = await client.GetInfoAsync();
    int w = info.DeviceWidth > 0 ? info.DeviceWidth : 640;
    int h = info.DeviceHeight > 0 ? info.DeviceHeight : 656;

    string path = PickBackground(backgroundsDir);
    byte[] jpeg = EncodeJpegForCanvas(path, w, h);
    Console.WriteLine($"Encoded {Path.GetFileName(path)} to {w}x{h} JPEG ({jpeg.Length} bytes). Sending...");
    var elapsed = await client.SendJpegAndWaitEchoAsync(jpeg, TimeSpan.FromSeconds(10));
    Console.WriteLine($"Echo received after {elapsed.TotalMilliseconds:F0} ms.");
}

static async Task FrameRateTestAsync(Mk20Client client, string backgroundsDir)
{
    var info = await client.GetInfoAsync();
    int w = info.DeviceWidth > 0 ? info.DeviceWidth : 640;
    int h = info.DeviceHeight > 0 ? info.DeviceHeight : 656;

    string path = PickBackground(backgroundsDir);
    byte[] jpeg = EncodeJpegForCanvas(path, w, h);

    Console.Write("Number of frames to send: ");
    int count = int.Parse(Console.ReadLine() ?? "20");

    var times = new List<double>();
    for (int i = 0; i < count; i++)
    {
        var elapsed = await client.SendJpegAndWaitEchoAsync(jpeg, TimeSpan.FromSeconds(10));
        times.Add(elapsed.TotalMilliseconds);
        Console.WriteLine($"  frame {i + 1}/{count}: {elapsed.TotalMilliseconds:F0} ms");
    }

    double avg = times.Average();
    Console.WriteLine($"Average round-trip: {avg:F1} ms  (~{1000.0 / avg:F1} fps self-clocked ceiling)");
}

static async Task PushFakeTelemetryAsync(Mk20Client client)
{
    var rnd = new Random();
    var data = new Dictionary<string, string>
    {
        ["gear"] = rnd.Next(1, 7).ToString(),
        ["speed"] = rnd.Next(0, 300).ToString(),
        ["rpm"] = rnd.Next(1000, 9000).ToString(),
        ["delta"] = (rnd.NextDouble() * 2 - 1).ToString("F2"),
    };
    Console.WriteLine("Pushing (cmd/method names here are UNVERIFIED for Layer A - see doc section 7.3): "
        + string.Join(", ", data.Select(kv => $"{kv.Key}={kv.Value}")));
    try
    {
        var reply = await client.PushSystemDataAsync(data);
        Console.WriteLine($"success={reply.Success} error={reply.ErrorString}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[expected-if-unsupported] {ex.Message}");
    }
}

static void ListenForKeys(Mk20Client client)
{
    Console.WriteLine("Listening for keyStateChanged events. Press Enter to stop.");
    Console.ReadLine();
}

static async Task PlayAudioAsync(Mk20Client client)
{
    Console.Write("On-device file path to play (e.g. a .wav uploaded earlier): ");
    string path = Console.ReadLine() ?? "";
    var reply = await client.PlayAudioAsync(path);
    Console.WriteLine($"success={reply.Success} error={reply.ErrorString}");
}
