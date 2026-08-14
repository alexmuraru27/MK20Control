using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Builds the full 20-key grid theme (see <see cref="FullGridThemeBuilderTests"/>) and
/// uploads it to a real device, activating it. Uploads to the fixed self-contained path
/// <see cref="DeviceThemeNames.FullGrid"/> by default (override via
/// <c>MK20_UPLOAD_THEME_NAME</c>). Requires <c>MK20_COM_PORT</c> - see
/// <see cref="HardwareConnection"/>. Formerly <c>Mk20Control.App</c> menu option 15.
/// </summary>
public class FullGridThemeUploadTests
{
    [Test]
    public async Task BuildAndUpload_ActivatesFullGridTheme()
    {
        string themeName = DeviceThemeNames.Resolve(DeviceThemeNames.FullGrid);

        byte[] encoded = FullGridThemeBuilderTests.BuildFullGridTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {themeName}...");
        await client.UploadThemeAsync(themeName, encoded, TimeSpan.FromSeconds(30));

        TestContext.WriteLine("Upload complete and theme activated - 20 keys now show icons 01-20 and type digits/letters.");
    }
}
