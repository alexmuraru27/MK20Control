using Microsoft.Extensions.Logging;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Transport;

namespace Mk20Control.IntegrationTests.Support;

/// <summary>
/// Opens a connection to a real, physically-attached MK20 device for hardware-visible
/// integration tests. The COM port is read from the <c>MK20_COM_PORT</c> environment
/// variable (e.g. <c>MK20_COM_PORT=COM7</c>) - tests cannot prompt interactively, unlike the
/// old console sandbox app. Every test that needs a device calls <see cref="OpenAsync"/> and
/// wraps its body in a <c>using</c>; if the environment variable isn't set, the test is
/// skipped (via <c>Assert.Ignore</c>) rather than failed, so the full suite can still run on
/// a machine with no MK20 attached.
/// </summary>
public static class HardwareConnection
{
    /// <summary>Name of the environment variable naming the serial port to connect through (e.g. "COM7").</summary>
    public const string ComPortEnvironmentVariable = "MK20_COM_PORT";

    /// <summary>
    /// Connects to the device named by the <c>MK20_COM_PORT</c> environment variable. Skips
    /// the calling test (via <see cref="NUnit.Framework.Assert.Ignore(string)"/>) if the
    /// variable isn't set - callers should call this first thing in a test body.
    /// </summary>
    public static async Task<Mk20DeviceClient> OpenAsync(ILoggerFactory? loggerFactory = null)
    {
        string? port = Environment.GetEnvironmentVariable(ComPortEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(port))
        {
            NUnit.Framework.Assert.Ignore(
                $"Set the {ComPortEnvironmentVariable} environment variable (e.g. \"COM7\") to run this test against real hardware.");
        }

        loggerFactory ??= LoggerFactory.Create(b => b
            .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
            .SetMinimumLevel(LogLevel.Debug));

        // Wrap the real serial transport with a wire-level logger - a live-USB-capture
        // substitute that records byte-for-byte exactly what this process writes/reads, so a
        // real hardware test session can be directly compared against confirmed real
        // captures (tools/Captures/*.pcapng) using the same message-sequence analysis
        // approach used throughout this project's investigation.
        string wireLogPath = Path.Combine(Path.GetTempPath(), $"mk20-wirelog-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        var innerTransport = new SerialPortTransport(port!, logger: loggerFactory.CreateLogger<SerialPortTransport>());
        var loggingTransport = new WireLoggingTransport(innerTransport, wireLogPath);
        var client = new Mk20DeviceClient(loggingTransport, logger: loggerFactory.CreateLogger<Mk20DeviceClient>());
        Console.WriteLine($"Wire-level log for this session: {wireLogPath}");

        await client.ConnectAsync();
        return client;
    }
}
