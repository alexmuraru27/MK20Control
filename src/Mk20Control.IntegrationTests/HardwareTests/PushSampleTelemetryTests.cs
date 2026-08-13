using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and pushes a handful of sample telemetry values via
/// <c>SEND_SYSTEM_DATA_TO_DEVICE</c>. Only has a visible effect if the currently loaded
/// theme has a matching <c>system_data_name</c> binding. Requires <c>MK20_COM_PORT</c> - see
/// <see cref="HardwareConnection"/>. Formerly <c>Mk20Control.App</c> menu option 6.
/// </summary>
public class PushSampleTelemetryTests
{
    [Test]
    public async Task PushSampleTelemetry_SendsSuccessfully()
    {
        await using var client = await HardwareConnection.OpenAsync();

        var rnd = new Random();
        var data = new Dictionary<string, string>
        {
            ["GPU Usage"] = $"{rnd.Next(0, 100)}%",
            ["CPU Usage"] = $"{rnd.Next(0, 100)}%",
            ["CPU Temperature"] = $"{rnd.Next(30, 80)}\u2103",
        };

        await client.PushSystemDataAsync(data);

        TestContext.WriteLine("Pushed: " + string.Join(", ", data.Select(kv => $"{kv.Key}={kv.Value}")));
        TestContext.WriteLine("(only has a visible effect if the currently loaded theme has a matching system_data_name binding)");
    }
}
