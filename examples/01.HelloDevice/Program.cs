using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Model;

namespace Mk20Control.Examples.HelloDevice
{
    /// <summary>
    /// Example 1 - Hello device.
    ///
    /// The smallest useful program: connect to the keypad, ask what it is, change
    /// its backlight, and list the themes installed on it. No theme building yet.
    ///
    /// Run with:  dotnet run --project examples/01.HelloDevice -- COM7
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string? port = ResolvePort(args);
            if (port is null)
            {
                return 1;
            }

            // CreateForSerialPort does not open the port; ConnectAsync does.
            // Disposing the client closes it, so "await using" is the safe way to hold one.
            await using Mk20DeviceClient client = Mk20DeviceClient.CreateForSerialPort(port);
            await client.ConnectAsync();

            // --- who are we talking to? -------------------------------------
            // TryPingAsync returns null instead of throwing when nothing answers,
            // which makes it the right way to confirm an MK20 is really there.
            DeviceIdentity? identity = await client.TryPingAsync();
            if (identity is null)
            {
                Console.WriteLine($"Nothing answered on {port}.");
                Console.WriteLine("Is the vendor app running? It holds the serial port exclusively.");
                return 1;
            }

            Console.WriteLine($"Connected to {identity.DeviceName} on {port}");
            Console.WriteLine($"  firmware   {identity.Version}");
            Console.WriteLine($"  screen     {identity.ScreenModel} {identity.ScreenWidth}x{identity.ScreenHeight}");
            Console.WriteLine($"  backlight  {identity.DeviceBacklight}%");
            Console.WriteLine($"  volume     {identity.DeviceVolume}");

            // Any field the library does not model yet is still readable as raw text.
            foreach ((string key, string value) in identity.RawFields)
            {
                Console.WriteLine($"  raw: {key} = {value}");
            }

            // --- what is installed on it? -----------------------------------
            // This is also how you discover the path to pass to ReloadThemeAsync.
            ThemeListing listing = await client.GetInstalledThemesAsync();

            Console.WriteLine();
            Console.WriteLine($"Storage: {listing.MegabytesAvailable:N0} of {listing.MegabytesTotal:N0} MB free");
            Console.WriteLine($"Installed themes ({listing.Themes.Count}):");

            foreach (InstalledTheme theme in listing.Themes)
            {
                Console.WriteLine($"  {theme.Path}  crc32=0x{theme.Crc32:x8}");
            }

            // --- drive the hardware -----------------------------------------
            // Sweep the backlight up and down until Ctrl+C, then put it back where it was.
            int original = identity.DeviceBacklight ?? 80;

            using CancellationTokenSource cancellation = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            // Optional "--seconds N" so the example can be run unattended.
            TimeSpan? runFor = ResolveDuration(args);
            if (runFor is { } limit)
            {
                cancellation.CancelAfter(limit);
            }

            Console.WriteLine();
            Console.WriteLine("Sweeping the backlight between 10% and 100%." +
                (runFor is null ? " Press Ctrl+C to stop." : $" Stopping after {runFor.Value.TotalSeconds:N0}s."));

            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    for (int level = 10; level <= 100; level += 5)
                    {
                        await client.SetBacklightAsync(level, cancellation.Token);
                        Console.Write($"\r  backlight {level,3}%   ");
                        await Task.Delay(60, cancellation.Token);
                    }

                    for (int level = 100; level >= 10; level -= 5)
                    {
                        await client.SetBacklightAsync(level, cancellation.Token);
                        Console.Write($"\r  backlight {level,3}%   ");
                        await Task.Delay(60, cancellation.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected: Ctrl+C, or the --seconds limit.
            }

            Console.WriteLine();
            Console.WriteLine($"Restoring to {original}%.");
            await client.SetBacklightAsync(original);

            return 0;
        }

        /// <summary>
        /// Optional "--seconds N" argument, so the example can be run unattended. Returns
        /// null when absent, meaning "run until Ctrl+C".
        /// </summary>
        private static TimeSpan? ResolveDuration(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] is "--seconds" && double.TryParse(args[i + 1], out double seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }

            return null;
        }

        /// <summary>
        /// Takes the port from the first argument, else the MK20_COM_PORT environment
        /// variable. Returns null (with an explanation) rather than throwing.
        /// </summary>
        private static string? ResolvePort(string[] args)
        {
            string? port = args.Length > 0 && !args[0].StartsWith("--")
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
