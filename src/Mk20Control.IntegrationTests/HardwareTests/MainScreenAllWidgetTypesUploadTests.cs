using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// SANDBOX - builds and uploads the every-widget-type main-screen theme (see
/// <see cref="MainScreenAllWidgetTypesThemeTests"/>), then pumps varied telemetry
/// (random/ramp/sine/cosine/counter patterns, per widget) plus a live clock (hour/minute/
/// second - the device's digital clock is host-driven, not RTC-driven, confirmed via a real
/// capture) for a fixed window, so every widget type's live rendering can be visually
/// confirmed at once. Uploads to the fixed self-contained path
/// <see cref="DevicePaths.MainScreenAllWidgetTypes"/> by default (override via
/// <c>MK20_UPLOAD_DEVICE_PATH</c>); optionally set <c>MK20_PUMP_SECONDS</c> (default 15).
/// Requires <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>. Formerly
/// <c>Mk20Control.App</c> menu option 18.
/// </summary>
public class MainScreenAllWidgetTypesUploadTests
{
    [Test]
    public async Task BuildUploadAndPump_AnimatesEveryWidgetType()
    {
        string devicePath = DevicePaths.Resolve(DevicePaths.MainScreenAllWidgetTypes);

        byte[] encoded = MainScreenAllWidgetTypesThemeTests.BuildTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath, encoded, TimeSpan.FromSeconds(30));
        TestContext.WriteLine("Upload complete and theme activated.");

        int seconds = int.TryParse(Environment.GetEnvironmentVariable("MK20_PUMP_SECONDS"), out int s) ? s : 15;
        var rnd = new Random();
        int tick = 0;
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        TestContext.WriteLine($"Pumping varied telemetry (test1-test9 + live clock) for {seconds} second(s).");
        while (DateTime.UtcNow < deadline)
        {
            tick++;
            var now = DateTime.Now;
            var data = new Dictionary<string, string>
            {
                ["test1"] = rnd.Next(0, 101).ToString(),
                ["test2"] = (tick * 7 % 101).ToString(),
                ["test3"] = ((int)(50 + 50 * Math.Sin(tick * 0.3))).ToString(),
                ["test4"] = rnd.Next(0, 101).ToString(),
                ["test5"] = (tick * 13 % 101).ToString(),
                ["test6"] = ((int)(50 + 50 * Math.Cos(tick * 0.25))).ToString(),
                ["test7"] = $"tick {tick}",
                ["test8"] = $"Line A {rnd.Next(0, 999)}\nLine B {rnd.Next(0, 999)}",
                ["test9"] = $"{rnd.Next(0, 300)} kph",
                // DigitalClockItem is host-driven, not RTC-driven - confirmed via
                // capture17_multiple_theme_set.pcapng (ScreenKeyWindows pushes
                // hour/minute/second every second). Push the same way here.
                ["hour"] = now.Hour.ToString(),
                ["minute"] = now.Minute.ToString(),
                ["second"] = now.Second.ToString(),
            };
            await client.PushSystemDataAsync(data);
            TestContext.WriteLine("Pushed: " + string.Join(", ", data.Select(kv => $"{kv.Key}={kv.Value}")));
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        TestContext.WriteLine("Done pumping telemetry.");
    }
}
