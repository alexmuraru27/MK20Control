using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and listens for key/notification events
/// (<c>DEVICE_ProactiveEscalationCMD</c>) for a fixed window, printing each one. Set
/// <c>MK20_LISTEN_SECONDS</c> to override the default 10-second listen window. Requires
/// <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>. Formerly
/// <c>Mk20Control.App</c> menu option 9 (there, Enter stopped listening; a test can't wait
/// for interactive input, so this uses a fixed timeout instead - press keys on the device
/// during the test run to see them logged).
/// </summary>
public class ListenForEventsTests
{
    [Test]
    public async Task ListenForEvents_LogsKeyPressesDuringWindow()
    {
        await using var client = await HardwareConnection.OpenAsync();

        var received = new List<string>();
        client.NotificationReceived += (_, e) =>
        {
            string description = $"{e.Position} pressed={e.IsPressed}" +
                (e.ActionDescriptor is { } d && d.TryGetValue("type", out var t) ? $" action={t.AsString}" : "");
            received.Add(description);
            TestContext.WriteLine($"[event] {description}");
        };

        int seconds = int.TryParse(Environment.GetEnvironmentVariable("MK20_LISTEN_SECONDS"), out int s) ? s : 10;
        TestContext.WriteLine($"Listening for {seconds} second(s) - press keys on the device now.");
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        TestContext.WriteLine($"Captured {received.Count} event(s) during the listen window.");
    }
}
