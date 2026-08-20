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

namespace Mk20Control.Examples.SimRacingButtonBox
{
    /// <summary>
    /// Example 4 - A sim racing button box.
    ///
    /// The most complete example: two top-level pages plus three folders, mixing every
    /// kind of key the device supports.
    ///
    /// It shows the three navigation mechanisms side by side:
    ///   * PreviousPage/NextPage - step through top-level pages in a ring
    ///   * OpenPage              - enter a folder
    ///   * OneLevelUp            - come back out of one
    ///
    /// A folder is just an ordinary page that names its parent via <c>.AsFolderOf(...)</c>.
    /// Without that the device will navigate in and refuse to come back out.
    ///
    /// Every button here uses a <c>Command</c> action, so the device reports each press to
    /// this program and one shared log prints which button it was:
    ///
    ///   [press] r0c3  racing.gear-up
    ///   [press] r3c0  FUEL (openPage)
    ///
    /// The argument to <c>Command</c> is the routing id <c>OnCommand</c> matches on, and it
    /// must be unique - which already makes it a readable name, so these keys need nothing
    /// else. The navigation keys are the ones that do: <c>OpenPage</c>, <c>OneLevelUp</c> and
    /// <c>PreviousPage</c>/<c>NextPage</c> are handled by the device itself and carry no id,
    /// so without <c>description:</c> all three folder keys would log as a bare "openPage".
    /// That is the field's real purpose - a label for actions that have no id of their own.
    ///
    /// The alternative is <c>KeyActions.Keyboard(...)</c>, which makes the device emit a real
    /// keystroke on its own - ideal for a sim with this program closed, but such a key is
    /// invisible to the host (confirmed on hardware: it types into the focused window and
    /// sends no event at all), so it can neither be logged nor handled here. Pick per key:
    /// keystrokes for things the sim binds directly, commands for anything the host must see.
    ///
    /// Run with:  dotnet run --project examples/04.SimRacingButtonBox -- COM7
    /// </summary>
    internal static class Program
    {
        private const string ThemeName = "example-racing";

        // Ids for the two buttons that also run their own handler, named once so the theme
        // and the binding cannot drift apart.
        private const string MarkLapCommandId = "racing.mark-lap";
        private const string FuelReportCommandId = "racing.fuel-report";

        private static async Task<int> Main(string[] args)
        {
            string? port = ResolvePort(args);
            if (port is null)
            {
                return 1;
            }

            ThemeFile theme = BuildTheme();

            await using Mk20DeviceClient client = Mk20DeviceClient.CreateForSerialPort(port);
            await client.ConnectAsync();

            Console.WriteLine($"Uploading button box '{ThemeName}' ...");
            await client.UploadThemeAsync(ThemeName, theme, TimeSpan.FromSeconds(60));
            Console.WriteLine("Uploaded and activated.");

            using KeyBindings bindings = new(client);
            BindHostSideCommands(bindings);

            // The device reports every page change, which is handy while laying a box out.
            client.PageSwitched += (_, _) => Console.WriteLine("[page] the active page changed");

            // The clock on the secondary screen has no on-device time source, so it only
            // ticks while this runs. See PumpClockAsync below.
            using CancellationTokenSource cancellation = new();
            Task clockPump = PumpClockAsync(client, cancellation.Token);

            Console.WriteLine();
            Console.WriteLine("Drive the box:");
            Console.WriteLine("  FUEL / AIDS / LIGHTS open folders, BACK leaves them");
            Console.WriteLine("  PREV / NEXT step between the two top-level pages");
            Console.WriteLine("Press Enter here to exit.");

            await Task.Run(Console.ReadLine);

            cancellation.Cancel();
            await clockPump;
            return 0;
        }

        /// <summary>
        /// Drives the secondary-screen clock.
        ///
        /// The device has no real-time clock of its own: the vendor software pushes "hour",
        /// "minute" and "second" once a second like any other telemetry value. A clock
        /// widget therefore shows a frozen time unless something keeps sending them.
        /// </summary>
        private static async Task PumpClockAsync(Mk20DeviceClient client, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);

                    DateTime now = DateTime.Now;

                    await client.PushSystemDataAsync(new Dictionary<string, string>
                    {
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

            // Pages must exist before they can be referenced, so create them all first.
            ThemePageBuilder drivingPage = builder.AddPage().SetCanvas(640, 656);
            ThemePageBuilder strategyPage = builder.AddPage().SetCanvas(640, 656);

            // A folder declares its parent. This is what makes BACK work.
            ThemePageBuilder pitFolder = builder.AddPage().SetCanvas(640, 656).AsFolderOf(drivingPage);
            ThemePageBuilder aidsFolder = builder.AddPage().SetCanvas(640, 656).AsFolderOf(drivingPage);
            ThemePageBuilder lightsFolder = builder.AddPage().SetCanvas(640, 656).AsFolderOf(strategyPage);

            BuildDrivingPage(drivingPage, pitFolder, aidsFolder);
            BuildStrategyPage(strategyPage, lightsFolder);
            BuildPitFolder(pitFolder);
            BuildAidsFolder(aidsFolder);
            BuildLightsFolder(lightsFolder);

            // Nothing else occupies the secondary screen on the driving page, so put a
            // clock there rather than leaving it blank.
            AddSecondaryScreenClock(drivingPage);

            AddEncoders(drivingPage);

            return builder.Build();
        }

        /// <summary>
        /// A HH:MM:SS clock centred on the secondary screen.
        ///
        /// The secondary screen is the top band of the canvas: 428x142 starting at x=106
        /// (the key grid begins below it, at y=144). A clock is built from one item per
        /// field, so three fields are laid out side by side and centred as a group.
        /// </summary>
        private static void AddSecondaryScreenClock(ThemePageBuilder page)
        {
            const double SecondaryLeft = 106;
            const double SecondaryWidth = 428;
            const double SecondaryHeight = 142;

            const double FieldWidth = 64;
            const double FieldHeight = 52;
            const double Gap = 10;

            const double TotalWidth = (FieldWidth * 3) + (Gap * 2);
            double left = SecondaryLeft + ((SecondaryWidth - TotalWidth) / 2);
            double top = (SecondaryHeight - FieldHeight) / 2;

            ThemeColor digits = ThemeColor.White;
            ThemeColor invisible = ThemeColor.Transparent;

            string[] fields = { "hour", "minute", "second" };

            for (int index = 0; index < fields.Length; index++)
            {
                string field = fields[index];
                double x = left + (index * (FieldWidth + Gap));

                page.AddDigitalClockField(clock => clock
                    .At(x, top, FieldWidth, FieldHeight, z: 2)
                    .Field(field)
                    .Font("Microsoft YaHei,28,-1,5,75,0,0,0,0,0")
                    .Colors(digits, invisible, invisible));
            }
        }

        /// <summary>Page 1 - the controls you reach for while driving.</summary>
        private static void BuildDrivingPage(
            ThemePageBuilder page,
            ThemePageBuilder pitFolder,
            ThemePageBuilder aidsFolder)
        {
            // Row 0 - the essentials.
            page.AddKey(0, 0, key => key
                .Icon("engine-start-stop.png", LoadIcon("engine-start-stop.png"))
                .Title("START")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                // The description is echoed back on every press, so the log can name the
                // button instead of printing a bare r0c0.
                .Action(KeyActions.Command("racing.start")));

            page.AddKey(0, 1, key => key
                .Icon("pit-limiter.png", LoadIcon("pit-limiter.png"))
                .Title("LIMITER")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.limiter")));

            page.AddKey(0, 2, key => key
                .Icon("neutral.png", LoadIcon("neutral.png"))
                .Title("NEUTRAL")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.neutral")));

            page.AddKey(0, 3, key => key
                .Icon("gear-up.png", LoadIcon("gear-up.png"))
                .Title("GEAR +")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.gear-up")));

            page.AddKey(0, 4, key => key
                .Icon("gear-down.png", LoadIcon("gear-down.png"))
                .Title("GEAR -")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.gear-down")));

            // Row 1 - driver aids and DRS.
            page.AddKey(1, 0, key => key
                .Icon("tc-toggle.png", LoadIcon("tc-toggle.png"))
                .Title("TC")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.tc")));

            page.AddKey(1, 1, key => key
                .Icon("abs-toggle.png", LoadIcon("abs-toggle.png"))
                .Title("ABS")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.abs")));

            page.AddKey(1, 2, key => key
                .Icon("drs-activate.png", LoadIcon("drs-activate.png"))
                .Title("DRS")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.drs")));

            page.AddKey(1, 3, key => key
                .Icon("headlights-on-off.png", LoadIcon("headlights-on-off.png"))
                .Title("LIGHTS")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.lights")));

            page.AddKey(1, 4, key => key
                .Icon("headlight-flash.png", LoadIcon("headlight-flash.png"))
                .Title("FLASH")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.flash")));

            // Row 2 - two buttons that also run their own handler, not just the shared log.
            page.AddKey(2, 0, key => key
                .Icon("horn.png", LoadIcon("horn.png"))
                .Title("MARK")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command(MarkLapCommandId)));

            page.AddKey(2, 1, key => key
                .Icon("fuel-to-end.png", LoadIcon("fuel-to-end.png"))
                .Title("REPORT")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command(FuelReportCommandId)));

            // Row 3 - navigation. A folder key names the page it opens; the device performs
            // the jump itself, so no handler is needed for these.
            page.AddKey(3, 0, key => key
                .IconDevice(DeviceIcon.OpenFolder)
                .Title("FUEL")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.OpenPage(pitFolder.PageId, description: "FUEL")));

            page.AddKey(3, 1, key => key
                .IconDevice(DeviceIcon.OpenFolder)
                .Title("AIDS")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.OpenPage(aidsFolder.PageId, description: "AIDS")));

            page.AddKey(3, 3, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("PREV")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.PreviousPage(description: "PREV")));

            page.AddKey(3, 4, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("NEXT")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.NextPage(description: "NEXT")));
        }

        /// <summary>Page 2 - slower, between-stint adjustments.</summary>
        private static void BuildStrategyPage(ThemePageBuilder page, ThemePageBuilder lightsFolder)
        {
            page.AddKey(0, 0, key => key
                .Icon("fuel-mix-lean.png", LoadIcon("fuel-mix-lean.png"))
                .Title("MIX -")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.mix-down")));

            page.AddKey(0, 1, key => key
                .Icon("fuel-mix-rich.png", LoadIcon("fuel-mix-rich.png"))
                .Title("MIX +")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.mix-up")));

            page.AddKey(0, 2, key => key
                .Icon("engine-map-decrease.png", LoadIcon("engine-map-decrease.png"))
                .Title("MAP -")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.map-down")));

            page.AddKey(0, 3, key => key
                .Icon("engine-map-increase.png", LoadIcon("engine-map-increase.png"))
                .Title("MAP +")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.map-up")));

            page.AddKey(0, 4, key => key
                .Icon("brake-bias-forward.png", LoadIcon("brake-bias-forward.png"))
                .Title("BIAS F")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.bias-forward")));

            page.AddKey(1, 0, key => key
                .Icon("brake-bias-rearward.png", LoadIcon("brake-bias-rearward.png"))
                .Title("BIAS R")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.bias-rear")));

            page.AddKey(1, 1, key => key
                .Icon("wiper-speed-increase.png", LoadIcon("wiper-speed-increase.png"))
                .Title("WIPE +")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.wipers-up")));

            page.AddKey(1, 2, key => key
                .Icon("wiper-speed-decrease.png", LoadIcon("wiper-speed-decrease.png"))
                .Title("WIPE -")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("racing.wipers-down")));

            page.AddKey(3, 0, key => key
                .IconDevice(DeviceIcon.OpenFolder)
                .Title("LIGHTS")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.OpenPage(lightsFolder.PageId, description: "LIGHTS")));

            page.AddKey(3, 3, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("PREV")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.PreviousPage(description: "PREV")));

            page.AddKey(3, 4, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("NEXT")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.NextPage(description: "NEXT")));
        }

        private static void BuildPitFolder(ThemePageBuilder folder)
        {
            folder.AddKey(0, 0, key => key
                .Icon("refuel-toggle.png", LoadIcon("refuel-toggle.png"))
                .Title("REFUEL")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("pit.refuel")));

            folder.AddKey(0, 1, key => key
                .Icon("fuel-amount-increase.png", LoadIcon("fuel-amount-increase.png"))
                .Title("FUEL +")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("pit.fuel-up")));

            folder.AddKey(0, 2, key => key
                .Icon("fuel-amount-decrease.png", LoadIcon("fuel-amount-decrease.png"))
                .Title("FUEL -")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("pit.fuel-down")));

            folder.AddKey(0, 3, key => key
                .Icon("fuel-to-end.png", LoadIcon("fuel-to-end.png"))
                .Title("TO END")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("pit.fuel-to-end")));

            folder.AddKey(0, 4, key => key
                .Icon("cancel-pit-request.png", LoadIcon("cancel-pit-request.png"))
                .Title("CANCEL")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("pit.cancel")));

            // The bottom-right cell is reserved for BACK by convention, matching the vendor's
            // own folders and leaving 19 usable cells.
            folder.AddKey(3, 4, key => key
                .IconDevice(DeviceIcon.OneLevelUp)
                .Title("BACK")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.OneLevelUp(description: "BACK")));
        }

        private static void BuildAidsFolder(ThemePageBuilder folder)
        {
            folder.AddKey(0, 0, key => key
                .Icon("abs-increase.png", LoadIcon("abs-increase.png"))
                .Title("ABS +")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("aids.abs-up")));

            folder.AddKey(0, 1, key => key
                .Icon("abs-decrease.png", LoadIcon("abs-decrease.png"))
                .Title("ABS -")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("aids.abs-down")));

            folder.AddKey(0, 2, key => key
                .Icon("tc-toggle.png", LoadIcon("tc-toggle.png"))
                .Title("TC")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("aids.tc")));

            folder.AddKey(0, 3, key => key
                .Icon("brake-bias-forward.png", LoadIcon("brake-bias-forward.png"))
                .Title("BIAS F")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("aids.bias-forward")));

            folder.AddKey(0, 4, key => key
                .Icon("brake-bias-rearward.png", LoadIcon("brake-bias-rearward.png"))
                .Title("BIAS R")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("aids.bias-rear")));

            folder.AddKey(3, 4, key => key
                .IconDevice(DeviceIcon.OneLevelUp)
                .Title("BACK")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.OneLevelUp(description: "BACK")));
        }

        private static void BuildLightsFolder(ThemePageBuilder folder)
        {
            folder.AddKey(0, 0, key => key
                .Icon("headlights-on-off.png", LoadIcon("headlights-on-off.png"))
                .Title("ON/OFF")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("lights.toggle")));

            folder.AddKey(0, 1, key => key
                .Icon("headlight-flash.png", LoadIcon("headlight-flash.png"))
                .Title("FLASH")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("lights.flash")));

            folder.AddKey(0, 2, key => key
                .Icon("wiper-speed-increase.png", LoadIcon("wiper-speed-increase.png"))
                .Title("WIPE +")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("lights.wipers-up")));

            folder.AddKey(0, 3, key => key
                .Icon("wiper-speed-decrease.png", LoadIcon("wiper-speed-decrease.png"))
                .Title("WIPE -")
                .TitleStyle(fontSize: 18, color: ThemeColor.White)
                .Action(KeyActions.Command("lights.wipers-down")));

            folder.AddKey(3, 4, key => key
                .IconDevice(DeviceIcon.OneLevelUp)
                .Title("BACK")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.OneLevelUp(description: "BACK")));
        }

        /// <summary>
        /// Both encoders. The left one sends a different keystroke per motion, which is
        /// the only way to tell clockwise from counter-clockwise; the right one uses a
        /// built-in function the device performs entirely on its own.
        /// </summary>
        private static void AddEncoders(ThemePageBuilder page)
        {
            page.AddEncoder(EncoderSide.Left, key => key
                .IconDevice(DeviceIcon.EncoderKeyboard)
                .Opacity(0)
                .Action(KeyActions.EncoderKeyboard(
                    rotateLeft: (KeyModifiers.None, HidKey.Comma),   // bias rearward
                    click: (KeyModifiers.None, HidKey.B),            // reset bias
                    rotateRight: (KeyModifiers.None, HidKey.Period)))); // bias forward

            page.AddEncoder(EncoderSide.Right, key => key
                .IconDevice(DeviceIcon.EncoderSystemVolume)
                .Opacity(0)
                .Action(KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)));
        }

        private static void BindHostSideCommands(KeyBindings bindings)
        {
            int lap = 0;

            // Everything without its own handler lands here - which is most of the box, plus
            // the navigation keys the device performs itself.
            bindings.Unbound += (_, context) => LogPress(context);

            // Buttons that do real work bind their id and log the same line first, so the
            // console shows every press regardless of which path handled it.
            bindings.OnCommand(MarkLapCommandId, context =>
            {
                LogPress(context);
                lap++;
                Console.WriteLine($"         -> lap {lap} flagged at {DateTime.Now:HH:mm:ss}");
            });

            bindings.OnCommand(FuelReportCommandId, context =>
            {
                LogPress(context);
                Console.WriteLine("         -> hook your telemetry source in here");
            });
        }

        /// <summary>
        /// Prints one line per press. The device reports only a row and column - never which
        /// page - so the readable name comes from the action descriptor the key carries back,
        /// set as <c>description:</c> when the theme was built.
        /// </summary>
        private static void LogPress(KeyEventContext context)
        {
            if (!context.IsPressed)
            {
                return;
            }

            // A Command key is identified by its id. A navigation key is executed by the
            // device and has none, so it falls back to the label given when the theme was built.
            string what = context.CommandId
                ?? $"{context.Action?.Description ?? "(unlabelled)"} ({context.Action?.RawType})";

            Console.WriteLine($"[press] r{context.Position.Row}c{context.Position.Column}  {what}");
        }

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
