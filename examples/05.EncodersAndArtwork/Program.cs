using System;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Host;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Theme.Building;

namespace Mk20Control.Examples.EncodersAndArtwork
{
    /// <summary>
    /// Example 5 - Encoders, backgrounds and transparent icons.
    ///
    /// Covers the presentation side of the device:
    ///   * an animated GIF background on BOTH screens
    ///   * key icons WITH an alpha channel, so the background shows through them
    ///   * every way an encoder can be bound
    ///
    /// Icons normally have no alpha: <c>Icon(...)</c> matches the vendor format and
    /// flattens transparency onto black. <c>IconPreservingAlpha(...)</c> keeps it, and
    /// the device composites the icon against whatever is behind the key. The top row
    /// below uses alpha and the second row uses the same artwork flattened, so the
    /// difference is visible side by side.
    ///
    /// Run with:  dotnet run --project examples/05.EncodersAndArtwork -- COM7
    /// </summary>
    internal static class Program
    {
        private const string DevicePath = "/data/theme/MK20/example-artwork/example-artwork.Theme";

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

            await using Mk20DeviceClient client = Mk20DeviceClient.CreateForSerialPort(port);
            await client.ConnectAsync();

            Console.WriteLine($"Uploading artwork demo to {DevicePath} ...");
            await client.UploadThemeFileAsync(DevicePath, ThemeFileCodec.Encode(theme), TimeSpan.FromSeconds(60));
            Console.WriteLine("Uploaded and activated.");

            Console.WriteLine();
            Console.WriteLine("Everything on the box now runs on the device itself - this program");
            Console.WriteLine("can be closed and the keys keep working:");
            Console.WriteLine();
            Console.WriteLine("  rows 1-4   type 1-9, 0, then A-I into whatever window has focus");
            Console.WriteLine("  row 3 col 1  the cat sends Ctrl+Alt+Del");
            Console.WriteLine("  left knob    system volume      right knob  screen brightness");
            Console.WriteLine();
            Console.WriteLine("The icons keep their alpha channel, so the animated background shows");
            Console.WriteLine("through every key - except row 4 col 5, left flattened for comparison.");
            Console.WriteLine();
            Console.WriteLine("Nothing is logged here: a device-native key sends no event to the host.");
            Console.WriteLine("Press Enter to exit.");

            await Task.Run(Console.ReadLine);
            return 0;
        }

        private static ThemeFile BuildTheme()
        {
            ThemeBuilder builder = new();

            builder.AddPage(page =>
            {
                page.SetCanvas(640, 656);

                // Backgrounds go in first so the keys composite on top of them.
                // The AutoFit variants resize/crop any source to the exact size each
                // screen needs (640x512 main, 428x142 secondary).
                page.AddDynamicImage(image => image
                    .MainScreenBackgroundAutoFit("background.gif", LoadAsset("mooglevibin.gif")));

                page.AddDynamicImage(image => image
                    .SecondaryScreenBackgroundAutoFit("secondary.gif", LoadAsset("pop-cat.gif")));

                // --- Row 0 - types 1-5.
                // Every key on this box uses IconPreservingAlpha, which keeps the icon's
                // transparency so the animated background shows straight through it. The
                // ordinary Icon(...) path flattens artwork onto black instead - r3c4 is left
                // that way on purpose, so you can see the difference on the device.
                page.AddKey(0, 0, key => key
                    .IconPreservingAlpha("alpha_ring.png", LoadAsset("alpha_ring.png"))
                    .Title("1")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit1, "1")));

                page.AddKey(0, 1, key => key
                    .IconPreservingAlpha("alpha_gradient.png", LoadAsset("alpha_gradient.png"))
                    .Title("2")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit2, "2")));

                page.AddKey(0, 2, key => key
                    .IconPreservingAlpha("alpha_checker.png", LoadAsset("alpha_checker.png"))
                    .Title("3")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit3, "3")));

                page.AddKey(0, 3, key => key
                    .IconPreservingAlpha("alpha_ring.png", LoadAsset("alpha_ring.png"))
                    .Title("4")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit4, "4")));

                page.AddKey(0, 4, key => key
                    .IconPreservingAlpha("alpha_gradient.png", LoadAsset("alpha_gradient.png"))
                    .Title("5")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit5, "5")));

                // --- Row 1 - types 6, 7, 8, 9, 0.
                page.AddKey(1, 0, key => key
                    .IconPreservingAlpha("alpha_ring.png", LoadAsset("alpha_ring.png"))
                    .Title("6")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit6, "6")));

                page.AddKey(1, 1, key => key
                    .IconPreservingAlpha("alpha_gradient.png", LoadAsset("alpha_gradient.png"))
                    .Title("7")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit7, "7")));

                page.AddKey(1, 2, key => key
                    .IconPreservingAlpha("alpha_checker.png", LoadAsset("alpha_checker.png"))
                    .Title("8")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit8, "8")));

                page.AddKey(1, 3, key => key
                    .IconPreservingAlpha("alpha_ring.png", LoadAsset("alpha_ring.png"))
                    .Title("9")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit9, "9")));

                page.AddKey(1, 4, key => key
                    .IconPreservingAlpha("alpha_checker.png", LoadAsset("alpha_checker.png"))
                    .Title("0")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.Digit0, "0")));

                // --- Row 2 - the combo key, then A-D.
                // Modifiers pack into the upper byte of the same keycode field a plain
                // keystroke uses, so Ctrl+Alt+Del is one action, not three. Its icon is an
                // animated GIF - a pressable key whose artwork moves.
                page.AddKey(2, 0, key => key
                    .AnimatedIcon("cat", LoadAsset("pop-cat.gif"))
                    .Title("CTRLALTDEL")
                    .TitleStyle(fontSize: 14, color: ThemeColor.White)
                    .Action(KeyActions.KeyboardCombo(
                        KeyModifiers.LeftCtrl | KeyModifiers.LeftAlt,
                        HidKey.Delete,
                        "L Ctrl L Alt Del")));

                page.AddKey(2, 1, key => key
                    .IconPreservingAlpha("alpha_gradient.png", LoadAsset("alpha_gradient.png"))
                    .Title("A")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.A, "A")));

                page.AddKey(2, 2, key => key
                    .IconPreservingAlpha("alpha_checker.png", LoadAsset("alpha_checker.png"))
                    .Title("B")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.B, "B")));

                page.AddKey(2, 3, key => key
                    .IconPreservingAlpha("alpha_ring.png", LoadAsset("alpha_ring.png"))
                    .Title("C")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.C, "C")));

                page.AddKey(2, 4, key => key
                    .IconPreservingAlpha("alpha_gradient.png", LoadAsset("alpha_gradient.png"))
                    .Title("D")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.D, "D")));

                // --- Row 3 - types E-I.
                page.AddKey(3, 0, key => key
                    .IconPreservingAlpha("alpha_checker.png", LoadAsset("alpha_checker.png"))
                    .Title("E")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.E, "E")));

                page.AddKey(3, 1, key => key
                    .IconPreservingAlpha("alpha_ring.png", LoadAsset("alpha_ring.png"))
                    .Title("F")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.F, "F")));

                page.AddKey(3, 2, key => key
                    .IconPreservingAlpha("alpha_gradient.png", LoadAsset("alpha_gradient.png"))
                    .Title("G")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.G, "G")));

                page.AddKey(3, 3, key => key
                    .IconPreservingAlpha("alpha_checker.png", LoadAsset("alpha_checker.png"))
                    .Title("H")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.H, "H")));

                // The one deliberately flattened key, for comparison: Icon() composites the
                // artwork onto black, so this cell hides the animation while every other
                // key lets it through. Same source PNG as the others.
                page.AddKey(3, 4, key => key
                    .Icon("alpha_ring.png", LoadAsset("alpha_ring.png"))
                    .Title("I")
                    .TitleStyle(fontSize: 18, color: ThemeColor.White)
                    .Action(KeyActions.Keyboard(HidKey.I, "I")));

                // --- The two knobs.
                // Both are built-in functions the device performs by itself, so they keep
                // working with this program closed. They are invisible: a built-in icon at
                // opacity 0, because the binding works regardless of what is drawn.
                page.AddEncoder(EncoderSide.Left, key => key
                    .IconAssetPath(EncoderPositions.SystemVolumeIcon)
                    .Opacity(0)
                    .Action(KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)));

                page.AddEncoder(EncoderSide.Right, key => key
                    .IconAssetPath(EncoderPositions.DeviceBrightnessIcon)
                    .Opacity(0)
                    .Action(KeyActions.EncoderFunction(EncoderFunctionType.DeviceBrightness)));
            });

            return builder.Build();
        }

        private static byte[] LoadAsset(string fileName) =>
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
