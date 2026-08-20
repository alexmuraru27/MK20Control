using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Host;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Theme.Building;

namespace Mk20Control.Examples.SystemMonitor
{
    /// <summary>
    /// Example 3 - A live system monitor.
    ///
    /// Builds a dashboard of gauges and text, then pushes values into it on a timer.
    ///
    /// Widgets do not fetch anything themselves. Each is bound by NAME with
    /// <c>.BoundTo("...")</c>, and your application pushes a dictionary of those names
    /// with <c>PushSystemDataAsync</c>. The names are entirely your choice.
    ///
    /// Values are sent as pre-formatted display strings ("42%", "3.1 GB"): the device
    /// shows the whole string as text and reads the leading number for a gauge's fill.
    ///
    /// Run with:  dotnet run --project examples/03.SystemMonitor -- COM7
    /// </summary>
    internal static class Program
    {
        private const string ThemeName = "example-monitor";

        // The names that tie a widget to a pushed value. Declared once so the theme and
        // the update loop always agree.
        private const string CpuChannel = "cpu_usage";
        private const string MemoryChannel = "ram_usage";
        private const string UptimeChannel = "uptime";

        // The dial is bound to its own channel rather than directly to CPU or RAM, so the
        // host decides at runtime which metric feeds it. The device just renders whatever
        // arrives under these names - that indirection is what makes the keys useful.
        private const string DialChannel = "dial";
        private const string DialLabelChannel = "dial_label";

        private const string CpuCommandId = "monitor.cpu";
        private const string RamCommandId = "monitor.ram";

        /// <summary>Which metric the main-screen dial is currently showing. Toggled by the two keys.</summary>
        private static volatile string _dialSource = CpuChannel;

        private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(500);

        private static async Task<int> Main(string[] args)
        {
            string? port = ResolvePort(args);
            if (port is null)
            {
                return 1;
            }

            // Build the dashboard first: a missing asset or bad layout should fail before
            // we touch the hardware.
            ThemeFile theme = BuildTheme();

            // "--save <path>" writes the .Theme file and exits without touching the device.
            string? savePath = ResolveSavePath(args);
            if (savePath is not null)
            {
                File.WriteAllBytes(savePath, ThemeFileCodec.Encode(theme));
                Console.WriteLine($"Wrote {savePath}");
                return 0;
            }

            await using Mk20DeviceClient client = Mk20DeviceClient.CreateForSerialPort(port);
            await client.ConnectAsync();

            Console.WriteLine($"Uploading dashboard '{ThemeName}' ...");
            await client.UploadThemeAsync(ThemeName, theme);
            Console.WriteLine("Uploaded and activated.");

            Console.WriteLine();
            Console.WriteLine($"Pushing values every {UpdateInterval.TotalMilliseconds:N0} ms. Press Ctrl+C to stop.");
            Console.WriteLine("Press CPU (top-left) or RAM (top-right) to choose what the dial shows.");

            // Key presses arrive on the device's event stream. Binding by command id means
            // the handler fires wherever that key sits, on any page. Handlers run on the
            // transport read thread, so these only set a field - the push loop picks the
            // change up on its next tick.
            using KeyBindings keys = new(client);

            keys.OnCommand(CpuCommandId, () =>
            {
                _dialSource = CpuChannel;
                Console.WriteLine();
                Console.WriteLine("  dial now showing CPU");
            });

            keys.OnCommand(RamCommandId, () =>
            {
                _dialSource = MemoryChannel;
                Console.WriteLine();
                Console.WriteLine("  dial now showing RAM");
            });

            using CancellationTokenSource cancellation = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            await PushValuesUntilCancelledAsync(client, cancellation.Token);

            Console.WriteLine("Stopped.");
            return 0;
        }

        /// <summary>
        /// Lays out the dashboard. Every item is positioned in canvas pixels from the
        /// top-left, and the 640x656 canvas covers two screens:
        ///
        ///   y 0-142    the secondary screen, a 428x142 strip starting at x=106.
        ///              Small text only - 9-10pt is what reads well there.
        ///   y 144-656  the main screen, a 5x4 grid of 128px key cells. Cell (row, col)
        ///              starts at x = col*128, y = 144 + row*128.
        ///
        /// Font size matters: the builder defaults to 72pt, which is enormous on a 142px
        /// strip, so every text item sets its own.
        /// </summary>
        private static ThemeFile BuildTheme()
        {
            ThemeBuilder builder = new();

            builder.AddPage(page =>
            {
                page.SetCanvas(640, 656);

                // --- secondary screen: CPU bar, RAM bar, uptime caption ----------
                page.AddText(text => text
                    .At(120, 8)
                    .BoundTo(CpuChannel)
                    .Font("Microsoft YaHei,10,-1,5,50,0,0,0,0,0")
                    .Color(ThemeColor.White));

                page.AddProgressBar(bar => bar
                    .At(120, 30, 400, 14)
                    .BoundTo(CpuChannel, 0, 100)
                    .Colors(new ThemeColor(0, 170, 255), ThemeColor.White.WithAlpha(60), ThemeColor.Black.WithAlpha(180)));

                page.AddText(text => text
                    .At(120, 56)
                    .BoundTo(MemoryChannel)
                    .Font("Microsoft YaHei,10,-1,5,50,0,0,0,0,0")
                    .Color(ThemeColor.White));

                page.AddProgressBar(bar => bar
                    .At(120, 78, 400, 14)
                    .BoundTo(MemoryChannel, 0, 100)
                    .Colors(new ThemeColor(120, 220, 120), ThemeColor.White.WithAlpha(60), ThemeColor.Black.WithAlpha(180)));

                page.AddText(text => text
                    .At(120, 104)
                    .BoundTo(UptimeChannel)
                    .Font("Microsoft YaHei,9,-1,5,50,0,0,0,0,0")
                    .Color(ThemeColor.White.WithAlpha(180)));

                // --- main screen: one dial, in the empty cell at row 1, column 2 --
                // That cell spans x 256-384, y 272-400. A radial gauge renders at
                // (radius * 2 * scale) px square anchored top-left, so at the default radius
                // of 100 and scale 0.4 it is 80px - hence x=280 to centre it on the cell.
                // ScreenLayout.KeyCell(1, 2) returns that rectangle if you would rather
                // compute positions than write them out.
                page.AddRadialGauge(gauge => gauge
                    .At(280, 290, scale: 0.4)
                    .BoundTo(DialChannel, 0, 100)
                    .AngleRange(225, 315)
                    .Gradient(new ThemeColor(0, 200, 120), new ThemeColor(255, 200, 0), new ThemeColor(255, 60, 60)));

                // Bound, not static: this caption changes with the selected metric.
                page.AddText(text => text
                    .At(304, 372)
                    .BoundTo(DialLabelChannel)
                    .Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0")
                    .Color(ThemeColor.White.WithAlpha(200)));

                // Two keys in the top row, at opposite corners. Each sends its identifier
                // back over serial; the handlers in Main pick those up and choose what the
                // dial shows.
                page.AddKey(0, 0, key => key
                    .Icon("icon_06.png", LoadIcon("icon_06.png"))
                    .Title("CPU")
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.Command(CpuCommandId)));

                page.AddKey(0, 4, key => key
                    .Icon("icon_07.png", LoadIcon("icon_07.png"))
                    .Title("RAM")
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.Command(RamCommandId)));
            });

            return builder.Build();
        }

        private static async Task PushValuesUntilCancelledAsync(Mk20DeviceClient client, CancellationToken token)
        {
            DateTime startedAt = DateTime.Now;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(UpdateInterval, token);

                    double cpu = ReadCpuUsagePercent();
                    double memory = ReadMemoryUsagePercent();
                    TimeSpan uptime = DateTime.Now - startedAt;

                    bool dialShowsCpu = _dialSource == CpuChannel;
                    double dial = dialShowsCpu ? cpu : memory;

                    // Keys are the names used by .BoundTo(...) when the theme was built.
                    Dictionary<string, string> values = new()
                    {
                        [CpuChannel] = $"{cpu:F0}%",
                        [MemoryChannel] = $"{memory:F0}%",
                        [UptimeChannel] = $"up {uptime:hh\\:mm\\:ss}",
                        [DialChannel] = $"{dial:F0}%",
                        [DialLabelChannel] = dialShowsCpu ? "CPU" : "RAM",
                    };

                    await client.PushSystemDataAsync(values);

                    Console.Write($"\r  CPU {cpu,5:F1}%   RAM {memory,5:F1}%   " +
                                  $"dial={(dialShowsCpu ? "CPU" : "RAM")}   {uptime:hh\\:mm\\:ss}   ");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
            }
        }

        private static DateTime _lastSampledAt = DateTime.UtcNow;
        private static TimeSpan _lastProcessorTime = TimeSpan.Zero;
        private static long _lastIdle, _lastKernel, _lastUser;

        /// <summary>
        /// Machine-wide CPU usage. On Windows this reads the kernel's own counters through
        /// <c>GetSystemTimes</c> - no NuGet package and no PerformanceCounter needed. On other
        /// platforms it falls back to this process's CPU time, normalised across cores.
        /// </summary>
        private static double ReadCpuUsagePercent()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user))
            {
                long idleDelta = idle - _lastIdle;
                long busyDelta = (kernel - _lastKernel) + (user - _lastUser);

                _lastIdle = idle;
                _lastKernel = kernel;
                _lastUser = user;

                // kernelTime already includes idle time, so total == kernel + user.
                return busyDelta > 0
                    ? Clamp(100.0 * (busyDelta - idleDelta) / busyDelta, 0, 100)
                    : 0;
            }

            return ReadProcessCpuUsagePercent();
        }

        /// <summary>This process's CPU usage, normalised across cores - the portable fallback.</summary>
        private static double ReadProcessCpuUsagePercent()
        {
            using Process process = Process.GetCurrentProcess();

            DateTime now = DateTime.UtcNow;
            TimeSpan processorTime = process.TotalProcessorTime;

            double elapsedMs = (now - _lastSampledAt).TotalMilliseconds;
            double usedMs = (processorTime - _lastProcessorTime).TotalMilliseconds;

            _lastSampledAt = now;
            _lastProcessorTime = processorTime;

            if (elapsedMs <= 0)
            {
                return 0;
            }

            double percent = usedMs / (elapsedMs * Environment.ProcessorCount) * 100.0;
            return Clamp(percent, 0, 100);
        }

        /// <summary>
        /// Machine-wide physical memory load. On Windows <c>GlobalMemoryStatusEx</c> reports it
        /// directly as a 0-100 percentage. Note the managed <see cref="GC.GetGCMemoryInfo()"/>
        /// is NOT usable for this: it only reports figures as of the last garbage collection,
        /// so it reads zero until one happens.
        /// </summary>
        private static double ReadMemoryUsagePercent()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NativeMethods.MemoryStatusEx status = new() { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
                if (NativeMethods.GlobalMemoryStatusEx(ref status))
                {
                    return Clamp(status.MemoryLoad, 0, 100);
                }
            }

            // GC.GetGCMemoryInfo is .NET Core only; on .NET Framework (Windows-only) the
            // GlobalMemoryStatusEx path above always applies.
            return 0;
        }

        /// <summary>Math.Clamp is .NET Core only.</summary>
        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        /// <summary>The two Win32 counters used above; both are in kernel32 and need no package.</summary>
        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct MemoryStatusEx
            {
                internal uint Length;
                internal uint MemoryLoad;
                internal ulong TotalPhys;
                internal ulong AvailPhys;
                internal ulong TotalPageFile;
                internal ulong AvailPageFile;
                internal ulong TotalVirtual;
                internal ulong AvailVirtual;
                internal ulong AvailExtendedVirtual;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
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
