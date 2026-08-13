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
            TestContext.WriteLine($"[key]  {description}");
        };

        // The device confirms every page change (relative paging, absolute jumpToPage,
        // entering a folder via openPage, or leaving one via oneLevelUp) with a SEND_JSON
        // frame carrying "themePageSwitch": true - the only feedback that a navigation key
        // actually did something, so log it alongside the raw press.
        int pageSwitches = 0;
        client.PageSwitched += (_, _) =>
        {
            pageSwitches++;
            TestContext.WriteLine("[page] device reports themePageSwitch -> the active page CHANGED");
        };

        int seconds = int.TryParse(Environment.GetEnvironmentVariable("MK20_LISTEN_SECONDS"), out int s) ? s : 10;
        TestContext.WriteLine($"Listening for {seconds} second(s) - press keys on the device now.");
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        TestContext.WriteLine($"Captured {received.Count} key event(s) and {pageSwitches} page switch(es) during the listen window.");
    }
}
