using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Builds the 5-key test theme (see <see cref="FiveKeyTestThemeTests"/>) and uploads it to
/// a real device, activating it. Uploads to the fixed self-contained path
/// <see cref="DevicePaths.FiveKeyTest"/> by default (override via
/// <c>MK20_UPLOAD_DEVICE_PATH</c>) - re-running this test overwrites the same theme instead
/// of accumulating a new one on the device's SD card each time. Requires
/// <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>. Formerly
/// <c>Mk20Control.App</c> menu option 14.
/// </summary>
public class FiveKeyThemeUploadTests
{
    [Test]
    public async Task BuildAndUpload_ActivatesFiveKeyTheme()
    {
        string devicePath = DevicePaths.Resolve(DevicePaths.FiveKeyTest);

        byte[] encoded = FiveKeyTestThemeTests.BuildFiveKeyTestTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath, encoded, TimeSpan.FromSeconds(20));

        TestContext.WriteLine("Upload complete and theme activated. Buttons 1-5 (top row) now show icons 01-05 and type '1'-'5'.");
    }
}
