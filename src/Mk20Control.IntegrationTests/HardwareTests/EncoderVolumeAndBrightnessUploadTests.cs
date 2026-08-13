using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Builds and uploads the left-encoder-volume / right-encoder-brightness theme (see
/// <see cref="EncoderVolumeAndBrightnessThemeTests"/>), then pumps varied Volume/device_bl
/// telemetry for a fixed window so the live progress-bar readouts can be visually confirmed
/// alongside physically turning each encoder. Uploads to the fixed self-contained path
/// <see cref="DevicePaths.EncoderVolumeAndBrightness"/> by default (override via
/// <c>MK20_UPLOAD_DEVICE_PATH</c>); optionally set <c>MK20_PUMP_SECONDS</c> (default 15).
/// Requires <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>.
/// </summary>
public class EncoderVolumeAndBrightnessUploadTests
{
    [Test]
    public async Task BuildUploadAndPump_AnimatesEncoderReadouts()
    {
        string devicePath = DevicePaths.Resolve(DevicePaths.EncoderVolumeAndBrightness);

        byte[] encoded = EncoderVolumeAndBrightnessThemeTests.BuildTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath, encoded, TimeSpan.FromSeconds(30));
        TestContext.WriteLine("Upload complete and theme activated. Try turning the left (volume) and right (brightness) encoders.");

        int seconds = int.TryParse(Environment.GetEnvironmentVariable("MK20_PUMP_SECONDS"), out int s) ? s : 15;
        var rnd = new Random();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        TestContext.WriteLine($"Pumping random Volume/device_bl telemetry for {seconds} second(s).");
        while (DateTime.UtcNow < deadline)
        {
            var data = new Dictionary<string, string>
            {
                ["Volume"] = rnd.Next(0, 101).ToString(),
                ["device_bl"] = rnd.Next(0, 101).ToString(),
            };
            await client.PushSystemDataAsync(data);
            TestContext.WriteLine("Pushed: " + string.Join(", ", data.Select(kv => $"{kv.Key}={kv.Value}")));
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        TestContext.WriteLine("Done pumping telemetry.");
    }
}
