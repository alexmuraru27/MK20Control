using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Host;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Theme.Building;

namespace Mk20Control.Examples.EveryBuildingBlock
{
    /// <summary>
    /// Example 6 - one of everything.
    ///
    /// A reference sheet you can hold in your hand: every widget the library can draw, every
    /// way to put an icon on a key, every action a key can carry, and both encoders - laid
    /// out so you can see each one live on the device and match it back to the line of code
    /// that produced it.
    ///
    /// Two layout rules make it readable rather than a soup of overlapping pixels:
    ///
    ///   * Every widget lives either on the secondary screen or inside ONE key cell. The
    ///     main screen is a 5x4 grid of 128px cells (see <c>ScreenLayout</c>), and nothing
    ///     here spills across a cell boundary - the background even draws the grid, so you
    ///     can see exactly which cell each widget occupies.
    ///   * Positions come from <c>ScreenLayout.KeyCell(row, column)</c>, never hand-counted
    ///     pixels, so a widget and the key beneath it can never drift apart.
    ///
    /// Both screens get their own background picture, and the whole thing is addressed by
    /// theme NAME - no device paths appear anywhere in this file.
    ///
    /// Page 1 shows the widgets, page 2 the key actions, and a folder hangs off page 2.
    /// </summary>
    internal static class Program
    {
        private const string ThemeName = "example-everything";

        // Channels this program pushes. The names are ours to choose - a widget shows
        // whatever we push under the name it was bound to.
        private const string BarChannel = "demo_bar";
        private const string DialChannel = "demo_dial";
        private const string RingChannel = "demo_ring";
        private const string TextChannel = "demo_text";

        private static readonly string SmallFont = "Microsoft YaHei,9,-1,5,50,0,0,0,0,0";
        private static readonly string LabelFont = "Microsoft YaHei,12,-1,5,50,0,0,0,0,0";
        private static readonly string ClockFont = "Microsoft YaHei,28,-1,5,75,0,0,0,0,0";

        private static async Task<int> Main(string[] args)
        {
            // "--save <path>" writes the .Theme file and exits without touching the device,
            // which is handy for inspecting it or opening it in another editor.
            string? savePath = ResolveSavePath(args);
            if (savePath is not null)
            {
                File.WriteAllBytes(savePath, Mk20Control.Protocol.Codecs.ThemeFileCodec.Encode(BuildTheme()));
                Console.WriteLine($"Wrote {savePath}");
                return 0;
            }

            string? port = ResolvePort(args);
            if (port is null)
            {
                return 1;
            }
            await using Mk20DeviceClient client = Mk20DeviceClient.CreateForSerialPort(port);
            await client.ConnectAsync();

            Console.WriteLine($"Uploading '{ThemeName}' ...");
            await client.UploadThemeAsync(ThemeName, BuildTheme(), TimeSpan.FromSeconds(60));
            Console.WriteLine("Uploaded and activated.");

            using KeyBindings bindings = new(client);
            bindings.Unbound += (_, context) => LogPress(context);
            bindings.OnCommand("demo.hello", () => Console.WriteLine("         -> hello from the handler"));
            bindings.OnCommand("demo.time", () => Console.WriteLine($"         -> it is {DateTime.Now:HH:mm:ss}"));

            client.PageSwitched += (_, _) => Console.WriteLine("[page] the active page changed");

            // Every widget on page 1 is data-bound, and the clock is host-fed too, so nothing
            // moves until this loop runs.
            using CancellationTokenSource cancellation = new();
            Task pump = PumpAsync(client, cancellation.Token);

            Console.WriteLine();
            Console.WriteLine("Page 1: every widget type. Page 2: every key action. NEXT/PREV move between them.");
            Console.WriteLine("Press Enter here to exit.");

            await Task.Run(Console.ReadLine);

            cancellation.Cancel();
            await pump;
            return 0;
        }

        /// <summary>
        /// Feeds every bound name once a second. The device draws only what it is given: stop
        /// this loop and the gauges hold their last value and the clock freezes.
        /// </summary>
        private static async Task PumpAsync(Mk20DeviceClient client, CancellationToken token)
        {
            int tick = 0;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);

                    tick++;

                    // Three different shapes so each gauge visibly moves on its own.
                    int sweep = tick % 101;
                    int bounce = Math.Abs(50 - (tick % 100)) * 2;
                    int sine = (int)(50 + (45 * Math.Sin(tick / 4.0)));

                    DateTime now = DateTime.Now;

                    await client.PushSystemDataAsync(new Dictionary<string, string>
                    {
                        [BarChannel] = $"{sweep}%",
                        [DialChannel] = $"{bounce}%",
                        [RingChannel] = $"{sine}%",
                        [TextChannel] = $"tick {tick}",
                        ["hour"] = now.ToString("HH"),
                        ["minute"] = now.ToString("mm"),
                        ["second"] = now.ToString("ss"),
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        private static ThemeFile BuildTheme()
        {
            ThemeBuilder builder = new();

            // Pages first, so keys can reference them by id.
            ThemePageBuilder widgets = builder.AddPage().SetCanvas(ScreenLayout.CanvasWidth, ScreenLayout.CanvasHeight);
            ThemePageBuilder actions = builder.AddPage().SetCanvas(ScreenLayout.CanvasWidth, ScreenLayout.CanvasHeight);
            ThemePageBuilder folder = builder.AddPage().SetCanvas(ScreenLayout.CanvasWidth, ScreenLayout.CanvasHeight).AsFolderOf(actions);

            BuildWidgetPage(widgets);
            BuildActionPage(actions, folder);
            BuildFolderPage(folder);

            return builder.Build();
        }

        /// <summary>
        /// Page 1 - every widget the library can draw, one per key cell, plus the
        /// secondary-screen ones.
        /// </summary>
        private static void BuildWidgetPage(ThemePageBuilder page)
        {
            // A still picture on each screen. Backgrounds go in first so everything else
            // composites on top. The main-screen image draws the 128px cell grid, which is
            // what makes "one widget per cell" visible at a glance.
            page.AddBackground(background => background.MainScreen("main_bg.png", LoadAsset("main_bg.png")));
            page.AddBackground(background => background.SecondaryScreen("secondary_bg.png", LoadAsset("secondary_bg.png")));

            // ---- Secondary screen: a title, a live value and a clock -------------------
            LayoutRect strip = ScreenLayout.SecondaryScreen;

            page.AddShadowText(text => text
                .At(strip.X + 12, strip.Y + 8)
                .Text("EVERY BLOCK")
                .Font("Microsoft YaHei,20,-1,5,75,0,0,0,0,0")
                .Color(ThemeColor.White)
                .Border(new ThemeColor(0, 80, 160), 3)
                .Shadow(ThemeColor.Black.WithAlpha(150), 6));

            page.AddText(text => text
                .At(strip.X + 14, strip.Y + 44)
                .BoundTo(TextChannel)
                .Font(SmallFont)
                .Color(new ThemeColor(0, 200, 255)));

            page.AddMultilineText(text => text
                .At(strip.X + 14, strip.Y + 62, 180, 70)
                .Text("widgets: 1 per cell\nkeys: page 2\nboth encoders: live")
                .Font(LabelFont)
                .Color(ThemeColor.White.WithAlpha(190)));

            // A clock is one item per field, so three of them sit side by side. There is no
            // letter-spacing setting - the digits are drawn inside each field's own box, so
            // the box IS the spacing. Make it too narrow and the two digits run together.
            // 64x52 at 28pt is about the smallest that still reads cleanly.
            page.AddDigitalClockField(clock => clock
                .At(strip.X + 226, strip.Y + 44, 64, 52, z: 2).Field("hour").Font(ClockFont)
                .Colors(ThemeColor.White, ThemeColor.Transparent, ThemeColor.Transparent));
            page.AddDigitalClockField(clock => clock
                .At(strip.X + 290, strip.Y + 44, 64, 52, z: 2).Field("minute").Font(ClockFont)
                .Colors(ThemeColor.White, ThemeColor.Transparent, ThemeColor.Transparent));
            page.AddDigitalClockField(clock => clock
                .At(strip.X + 354, strip.Y + 44, 64, 52, z: 2).Field("second").Font(ClockFont)
                .Colors(new ThemeColor(0, 200, 255), ThemeColor.Transparent, ThemeColor.Transparent));

            // ---- Row 0: five of the gauges, one per cell -------------------------------
            LayoutRect cell = ScreenLayout.KeyCell(0, 0);
            AddCellLabel(page, cell, "PROGRESS");
            page.AddProgressBar(bar => bar
                .At(cell.X + 10, cell.Y + 58, 108, 16)
                .BoundTo(BarChannel, 0, 100)
                .Colors(new ThemeColor(0, 170, 255), ThemeColor.White.WithAlpha(40), ThemeColor.Black.WithAlpha(160)));

            cell = ScreenLayout.KeyCell(0, 1);
            AddCellLabel(page, cell, "LINEAR");
            page.AddLinearGauge(gauge => gauge
                .At(cell.X + 10, cell.Y + 58, 108, 16)
                .BoundTo(BarChannel, 0, 100)
                .Colors(new ThemeColor(0, 220, 120), ThemeColor.White.WithAlpha(40), ThemeColor.Black.WithAlpha(160)));

            cell = ScreenLayout.KeyCell(0, 2);
            AddCellLabel(page, cell, "RADIAL");
            // A radial gauge draws at radius x 2 x scale, anchored top-left: 100x0.5x2 = 100px.
            page.AddRadialGauge(gauge => gauge
                .At(cell.X + 14, cell.Y + 22, scale: 0.5)
                .BoundTo(DialChannel, 0, 100)
                .Gradient(new ThemeColor(0, 170, 255), new ThemeColor(255, 200, 0), new ThemeColor(255, 60, 60)));

            cell = ScreenLayout.KeyCell(0, 3);
            AddCellLabel(page, cell, "CIRCULAR");
            page.AddCircularGauge(gauge => gauge
                .At(cell.X + 14, cell.Y + 22)
                .BoundTo(RingChannel, 0, 100)
                .Colors(new ThemeColor(0, 255, 140), new ThemeColor(40, 40, 40))
                .Geometry(margin: 10, radius: 50));

            cell = ScreenLayout.KeyCell(0, 4);
            AddCellLabel(page, cell, "SEGMENTED");
            page.AddSegmentedCircularGauge(gauge => gauge
                .At(cell.X + 14, cell.Y + 22)
                .BoundTo(RingChannel, 0, 100)
                .Colors(new ThemeColor(255, 170, 0), new ThemeColor(40, 40, 40))
                .Geometry(margin: 10, radius: 50));

            // ---- Row 1: the remaining widgets ------------------------------------------
            cell = ScreenLayout.KeyCell(1, 0);
            AddCellLabel(page, cell, "LIGHT-SHADOW");
            page.AddLightShadowGauge(gauge => gauge
                .At(cell.X + 14, cell.Y + 22)
                .BoundTo(DialChannel, 0, 100)
                .Colors(new ThemeColor(30, 30, 30), new ThemeColor(0, 200, 255), arcWidth: 8)
                .Geometry(radius: 50)
                .LightShadow(new ThemeColor(0, 200, 255)));

            cell = ScreenLayout.KeyCell(1, 1);
            AddCellLabel(page, cell, "TEXT");
            page.AddText(text => text
                .At(cell.X + 12, cell.Y + 54)
                .BoundTo(TextChannel)
                .Font(SmallFont)
                .Color(ThemeColor.White));

            cell = ScreenLayout.KeyCell(1, 2);
            AddCellLabel(page, cell, "MULTILINE");
            page.AddMultilineText(text => text
                .At(cell.X + 10, cell.Y + 34, 108, 80)
                .Text("wraps inside\nits own cell\nand no wider")
                .Font(LabelFont)
                .Color(new ThemeColor(0, 255, 200)));

            cell = ScreenLayout.KeyCell(1, 3);
            AddCellLabel(page, cell, "SHADOW");
            page.AddShadowText(text => text
                .At(cell.X + 16, cell.Y + 44)
                .Text("Aa")
                .Font("Microsoft YaHei,34,-1,5,75,0,0,0,0,0")
                .Color(new ThemeColor(255, 220, 0))
                .Border(new ThemeColor(20, 50, 200), 3)
                .Shadow(ThemeColor.Black.WithAlpha(150), 8));

            cell = ScreenLayout.KeyCell(1, 4);
            AddCellLabel(page, cell, "GIF");
            page.AddDynamicImage(image => image
                .At(cell.X + 14, cell.Y + 28, 100, 88)
                .Gif("pop-cat.gif", LoadAsset("pop-cat.gif")));

            // ---- Row 2: every way to put an icon on a key -------------------------------
            page.AddKey(2, 0, key => key
                .Icon("icon_08.png", LoadAsset("icon_08.png"))
                .Title("Icon")
                .Action(KeyActions.Command("demo.icon")));

            page.AddKey(2, 1, key => key
                .IconPreservingAlpha("alpha_ring.png", LoadAsset("alpha_ring.png"))
                .Opacity(0)
                .Title("Alpha")
                .Action(KeyActions.Command("demo.alpha")));

            page.AddKey(2, 2, key => key
                .AnimatedIcon("cat", LoadAsset("pop-cat.gif"))
                .Title("Animated")
                .Action(KeyActions.Command("demo.animated")));

            page.AddKey(2, 3, key => key
                .IconDevice(DeviceIcon.Keyboard)
                .Title("Built-in")
                .Action(KeyActions.Command("demo.builtin")));

            page.AddKey(2, 4, key => key
                .Title("Styled")
                .TitleStyle(fontSize: 16, color: new ThemeColor(255, 200, 0))
                .Action(KeyActions.Command("demo.styled")));

            // ---- Row 3: leaving this page ----------------------------------------------
            page.AddKey(3, 0, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("NEXT")
                .Action(KeyActions.NextPage(description: "NEXT")));

            page.AddKey(3, 4, key => key
                .Title("HELLO")
                .Action(KeyActions.Command("demo.hello")));

            // ---- Both encoders ----------------------------------------------------------
            // The left one types a different keystroke per motion - the only way to tell
            // clockwise from counter-clockwise. The right one runs a built-in device
            // function, so it keeps working with this program closed.
            page.AddEncoder(EncoderSide.Left, key => key
                .IconDevice(DeviceIcon.EncoderKeyboard)
                .Opacity(0)
                .Action(KeyActions.EncoderKeyboard(
                    rotateLeft: (KeyModifiers.None, HidKey.Comma),
                    click: (KeyModifiers.None, HidKey.B),
                    rotateRight: (KeyModifiers.None, HidKey.Period))));

            page.AddEncoder(EncoderSide.Right, key => key
                .IconDevice(DeviceIcons.ForEncoderFunction(EncoderFunctionType.SystemVolume))
                .Opacity(0)
                .Action(KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)));
        }

        /// <summary>Page 2 - one key per action type, so you can press each and watch the log.</summary>
        private static void BuildActionPage(ThemePageBuilder page, ThemePageBuilder folder)
        {
            // An animated background instead of a still one - the other way to dress a screen.
            page.AddDynamicImage(image => image.MainScreenBackgroundAutoFit("main_bg.png", LoadAsset("main_bg.png")));
            page.AddDynamicImage(image => image.SecondaryScreenBackgroundAutoFit("pop-cat.gif", LoadAsset("pop-cat.gif")));

            LayoutRect strip = ScreenLayout.SecondaryScreen;
            page.AddShadowText(text => text
                .At(strip.X + 12, strip.Y + 8)
                .Text("ACTIONS")
                .Font("Microsoft YaHei,20,-1,5,75,0,0,0,0,0")
                .Color(ThemeColor.White)
                .Border(new ThemeColor(0, 80, 160), 3)
                .Shadow(ThemeColor.Black.WithAlpha(150), 6));

            // Reports to this program, and a handler picks it up by id.
            page.AddKey(0, 0, key => key
                .Title("Command")
                .Action(KeyActions.Command("demo.time")));

            // The device types these itself - they work with this program closed, but the
            // host never sees them, so they cannot be logged below.
            page.AddKey(0, 1, key => key
                .Title("Key F5")
                .Action(KeyActions.Keyboard(HidKey.F5)));

            page.AddKey(0, 2, key => key
                .Title("Ctrl+S")
                .Action(KeyActions.KeyboardCombo(KeyModifiers.LeftCtrl, HidKey.S)));

            page.AddKey(0, 3, key => key
                .Title("Type")
                .Action(KeyActions.TypeText("MK20 ", pressEnterAfter: false)));

            page.AddKey(0, 4, key => key
                .IconDevice(DeviceIcon.OpenFolder)
                .Title("Folder")
                .Action(KeyActions.OpenPage(folder.PageId, description: "FOLDER")));

            // Absolute jump, as opposed to the relative PREV/NEXT below.
            page.AddKey(1, 0, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("Jump 1")
                .Action(KeyActions.JumpToPage(0, description: "JUMP")));

            page.AddKey(3, 0, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("PREV")
                .Action(KeyActions.PreviousPage(description: "PREV")));
        }

        /// <summary>A folder is just a page that named its parent - the only way back out is OneLevelUp.</summary>
        private static void BuildFolderPage(ThemePageBuilder page)
        {
            page.AddBackground(background => background.MainScreen("main_bg.png", LoadAsset("main_bg.png")));
            page.AddBackground(background => background.SecondaryScreen("secondary_bg.png", LoadAsset("secondary_bg.png")));

            LayoutRect strip = ScreenLayout.SecondaryScreen;
            page.AddText(text => text
                .At(strip.X + 14, strip.Y + 20)
                .Text("inside a folder")
                .Font("Microsoft YaHei,16,-1,5,50,0,0,0,0,0")
                .Color(ThemeColor.White));

            page.AddKey(0, 0, key => key
                .IconPreservingAlpha("alpha_gradient.png", LoadAsset("alpha_gradient.png"))
                .Opacity(0)
                .Title("Deep")
                .Action(KeyActions.Command("folder.deep")));

            page.AddKey(3, 4, key => key
                .IconDevice(DeviceIcon.OneLevelUp)
                .Title("BACK")
                .Action(KeyActions.OneLevelUp(description: "BACK")));
        }

        /// <summary>A caption inside the same cell as the widget it names.</summary>
        private static void AddCellLabel(ThemePageBuilder page, LayoutRect cell, string caption) =>
            page.AddText(text => text
                .At(cell.X + 8, cell.Y + 6)
                .Text(caption)
                .Font(LabelFont)
                .Color(new ThemeColor(120, 190, 255)));

        private static void LogPress(KeyEventContext context)
        {
            if (!context.IsPressed)
            {
                return;
            }

            string what = context.CommandId
                ?? $"{context.Action?.Description ?? "(unlabelled)"} ({context.Action?.RawType})";

            Console.WriteLine($"[press] r{context.Position.Row}c{context.Position.Column}  {what}");
        }

        /// <summary>Optional "--save &lt;path&gt;": write the theme file and exit, without connecting.</summary>
        private static string? ResolveSavePath(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--save")
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static byte[] LoadAsset(string fileName) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", fileName));

        private static string? ResolvePort(string[] args)
        {
            string? port = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
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
