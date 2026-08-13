using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// SANDBOX - builds and uploads the cat-GIF + every-gauge-type secondary-screen theme (see
/// <see cref="SecondaryScreenGaugesOverlayThemeTests"/>), then pumps random CPU/RAM/GPU
/// Usage telemetry for a fixed window so the overlay's live rendering over the animated GIF
/// can be visually confirmed. Uploads to the fixed self-contained path
/// <see cref="DevicePaths.SecondaryScreenGaugesOverlay"/> by default (override via
/// <c>MK20_UPLOAD_DEVICE_PATH</c>); optionally set <c>MK20_PUMP_SECONDS</c> (default 15).
/// Requires <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>. Formerly
/// <c>Mk20Control.App</c> menu option 17.
/// </summary>
public class SecondaryScreenGaugesOverlayUploadTests
{
    [Test]
    public async Task BuildUploadAndPump_AnimatesOverlaidGauges()
    {
        string devicePath = DevicePaths.Resolve(DevicePaths.SecondaryScreenGaugesOverlay);

        byte[] encoded = SecondaryScreenGaugesOverlayThemeTests.BuildTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath, encoded, TimeSpan.FromSeconds(30));
        TestContext.WriteLine("Upload complete and theme activated.");

        int seconds = int.TryParse(Environment.GetEnvironmentVariable("MK20_PUMP_SECONDS"), out int s) ? s : 15;
        var rnd = new Random();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        TestContext.WriteLine($"Pumping random telemetry (CPU/RAM/GPU Usage) for {seconds} second(s).");
        while (DateTime.UtcNow < deadline)
        {
            var data = new Dictionary<string, string>
            {
                ["CPU Usage"] = rnd.Next(0, 101).ToString(),
                ["RAM Usage"] = rnd.Next(0, 101).ToString(),
                ["GPU Usage"] = rnd.Next(0, 101).ToString(),
            };
            await client.PushSystemDataAsync(data);
            TestContext.WriteLine("Pushed: " + string.Join(", ", data.Select(kv => $"{kv.Key}={kv.Value}")));
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        TestContext.WriteLine("Done pumping telemetry.");
    }
}
