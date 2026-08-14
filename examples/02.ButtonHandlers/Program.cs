using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Host;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Theme.Building;

namespace Mk20Control.Examples.ButtonHandlers
{
    /// <summary>
    /// Example 2 - Buttons that run your own C#.
    ///
    /// Builds a small page, uploads it, then reacts to key presses in this process.
    ///
    /// The idea: each key carries a COMMAND ID you invent. The device does not act on
    /// those keys - it reports the press with the id attached, and KeyBindings routes
    /// it to your handler. Ids are page-agnostic, so moving a key to another cell,
    /// page or folder never breaks a binding.
    ///
    /// Run with:  dotnet run --project examples/02.ButtonHandlers -- COM7
    /// </summary>
    internal static class Program
    {
        private const string DevicePath = "/data/theme/MK20/example-buttons/example-buttons.Theme";

        private static async Task<int> Main(string[] args)
        {
            string? port = ResolvePort(args);
            if (port is null)
            {
                return 1;
            }

            // Build the theme first: a missing asset or bad layout should fail before we
            // touch the hardware.
            ThemeFile theme = BuildTheme();

            // "--save <path>" writes the .Theme file and exits without touching the device,
            // which is handy for inspecting it or opening it in another editor.
            string? savePath = ResolveSavePath(args);
            if (savePath is not null)
            {
                File.WriteAllBytes(savePath, ThemeFileCodec.Encode(theme));
                Console.WriteLine($"Wrote {savePath}");
                return 0;
            }

            await using Mk20DeviceClient client = Mk20DeviceClient.CreateForSerialPort(port);
            await client.ConnectAsync();

            Console.WriteLine($"Uploading theme to {DevicePath} ...");
            await client.UploadThemeFileAsync(DevicePath, ThemeFileCodec.Encode(theme));
            Console.WriteLine("Uploaded and activated.");

            using KeyBindings bindings = new(client);
            TaskCompletionSource quitRequested = new();

            // One binding per button. The id is the only thing that matters - it is
            // page-agnostic, so moving a key to another cell, page or folder keeps working.
            bindings.OnCommand("demo.hello", () => Console.WriteLine("[press] HELLO - hello!"));

            bindings.OnCommand("demo.time", () =>
                Console.WriteLine($"[press] TIME  - it is {DateTime.Now:HH:mm:ss}"));

            bindings.OnCommand("demo.dir", () =>
                Console.WriteLine($"[press] FILES - {Directory.GetCurrentDirectory()}"));

            bindings.OnCommand("demo.beep", () =>
            {
                Console.WriteLine("[press] BEEP");
                Console.Beep();
            });

            bindings.OnCommand("demo.quit", () =>
            {
                Console.WriteLine("[press] QUIT  - quitting...");
                quitRequested.TrySetResult();
            });

            // Press and release are bound separately, so a key can act like a momentary
            // switch rather than a trigger.
            bindings.OnCommandRelease("demo.hello", () => Console.WriteLine("        HELLO released"));

            // Anything reported that you have not bound lands here.
            bindings.Unbound += (_, context) =>
                Console.WriteLine($"[other] r{context.Position.Row}c{context.Position.Column} " +
                                  $"pressed={context.IsPressed} id={context.CommandId ?? "(none)"}");

            Console.WriteLine();
            Console.WriteLine("Press the keys on the device. The QUIT key ends the program.");
            Console.WriteLine("COPY types Ctrl+C by itself, so it never reaches a handler.");
            Console.WriteLine("Handlers run on the transport read thread, so keep them short.");

            await quitRequested.Task;
            return 0;
        }

        private static ThemeFile BuildTheme()
        {
            ThemeBuilder builder = new();

            builder.AddPage(page =>
            {
                page.SetCanvas(640, 656);

                // Every key here uses the same icon file. Each embedded icon is normalised to
                // 128x128 RGB and costs roughly 5 KB, and registering identical bytes twice
                // reuses the one asset rather than duplicating it - so this costs 5 KB total,
                // not 30 KB.
                //
                // Command(id) is what makes a key reportable: the device does not act on it,
                // it just echoes the id back on press, and KeyBindings routes it to the
                // matching handler in Main.
                page.AddKey(0, 0, key => key
                    .Icon("icon_01.png", LoadIcon("icon_01.png"))
                    .Title("HELLO")
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.Command("demo.hello")));

                page.AddKey(0, 1, key => key
                    .Icon("icon_01.png", LoadIcon("icon_01.png"))
                    .Title("TIME")
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.Command("demo.time")));

                page.AddKey(0, 2, key => key
                    .Icon("icon_01.png", LoadIcon("icon_01.png"))
                    .Title("FILES")
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.Command("demo.dir")));

                page.AddKey(0, 3, key => key
                    .Icon("icon_01.png", LoadIcon("icon_01.png"))
                    .Title("BEEP")
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.Command("demo.beep")));

                page.AddKey(0, 4, key => key
                    .Icon("icon_01.png", LoadIcon("icon_01.png"))
                    .Title("QUIT")
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.Command("demo.quit")));

                // A key the DEVICE performs by itself. It still types Ctrl+C once this
                // program exits - unlike the command keys above, which need a listener.
                // The trade-off: it is invisible to the host, so no handler can see it.
                page.AddKey(1, 0, key => key
                    .Icon("icon_01.png", LoadIcon("icon_01.png"))
                    .Title("COPY")
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.KeyboardCombo(KeyModifiers.LeftCtrl, HidKey.C)));
            });

            return builder.Build();
        }

        /// <summary>Optional "--save &lt;path&gt;": write the theme file and exit, without connecting.</summary>
        private static string? ResolveSavePath(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is "--save")
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        /// <summary>
        /// Reads an icon from this example's own icons folder, which the .csproj copies
        /// next to the binary - the example needs nothing from outside its directory.
        /// </summary>
        private static byte[] LoadIcon(string fileName) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "icons", fileName));

        private static string? ResolvePort(string[] args)
        {
            string? port = args.Length > 0
                ? args[0]
                : Environment.GetEnvironmentVariable("MK20_COM_PORT");

            if (!string.IsNullOrWhiteSpace(port))
            {
                return port;
            }

            string[] available = SerialPort.GetPortNames();

            Console.Error.WriteLine("No serial port given.");
            Console.Error.WriteLine("  pass one:  dotnet run -- COM7");
            Console.Error.WriteLine("  or set:    $env:MK20_COM_PORT = \"COM7\"");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Available ports: " +
                (available.Length > 0 ? string.Join(", ", available) : "(none found)"));

            return null;
        }
    }
}
