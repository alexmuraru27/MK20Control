using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Builds the full 20-key grid theme (see <see cref="FullGridThemeBuilderTests"/>) and
/// uploads it to a real device, activating it. Set <c>MK20_UPLOAD_DEVICE_PATH</c> to the
/// destination device-side path (e.g. <c>/data/theme/MK20/mygrid/mygrid.Theme</c>) - the
/// test is skipped if it isn't set. Requires <c>MK20_COM_PORT</c> - see
/// <see cref="HardwareConnection"/>. Formerly <c>Mk20Control.App</c> menu option 15.
/// </summary>
public class FullGridThemeUploadTests
{
    [Test]
    public async Task BuildAndUpload_ActivatesFullGridTheme()
    {
        string? devicePath = Environment.GetEnvironmentVariable("MK20_UPLOAD_DEVICE_PATH");
        if (string.IsNullOrWhiteSpace(devicePath))
            Assert.Ignore("Set MK20_UPLOAD_DEVICE_PATH (e.g. /data/theme/MK20/mygrid/mygrid.Theme) to run this test.");

        byte[] encoded = FullGridThemeBuilderTests.BuildFullGridTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath!, encoded, TimeSpan.FromSeconds(30));

        TestContext.WriteLine("Upload complete and theme activated - 20 keys now show icons 01-20 and type digits/letters.");
    }
}
