using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Builds the 5-key test theme (see <see cref="FiveKeyTestThemeTests"/>) and uploads it to
/// a real device, activating it. Set <c>MK20_UPLOAD_DEVICE_PATH</c> to the destination
/// device-side path (e.g. <c>/data/theme/MK20/test5/test5.Theme</c>) - the test is skipped
/// if it isn't set. Requires <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>.
/// Formerly <c>Mk20Control.App</c> menu option 14.
/// </summary>
public class FiveKeyThemeUploadTests
{
    [Test]
    public async Task BuildAndUpload_ActivatesFiveKeyTheme()
    {
        string? devicePath = Environment.GetEnvironmentVariable("MK20_UPLOAD_DEVICE_PATH");
        if (string.IsNullOrWhiteSpace(devicePath))
            Assert.Ignore("Set MK20_UPLOAD_DEVICE_PATH (e.g. /data/theme/MK20/test5/test5.Theme) to run this test.");

        byte[] encoded = FiveKeyTestThemeTests.BuildFiveKeyTestTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath!, encoded, TimeSpan.FromSeconds(20));

        TestContext.WriteLine("Upload complete and theme activated. Buttons 1-5 (top row) now show icons 01-05 and type '1'-'5'.");
    }
}
