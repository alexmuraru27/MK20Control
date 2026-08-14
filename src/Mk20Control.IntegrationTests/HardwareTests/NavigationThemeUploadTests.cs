using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Builds the page-navigation demo theme (see <see cref="NavigationThemeBuilderTests"/>) and
/// uploads it to a real device, activating it - the hardware-visible check that all four
/// confirmed navigation styles actually behave as decoded: relative previous/next paging,
/// absolute <c>jumpToPage</c>, entering a folder via <c>openPage</c>, and returning from it
/// via <c>oneLevelUp</c>.
///
/// What to press once it activates (4x5 grid):
/// <list type="number">
/// <item>Page 1 (hub) top-left "PAGE 1" and next-to-it "PAGE 2" jump straight to those pages.</item>
/// <item>On either ring page, bottom-left/bottom-right page backwards/forwards through the
/// ring, and top-right "HOME" jumps back to the hub from anywhere.</item>
/// <item>Hub "FOLDER" enters the folder page; its bottom-right "BACK" returns to the hub.</item>
/// </list>
///
/// Uploads to the fixed self-contained path <see cref="DeviceThemeNames.Navigation"/> by default
/// (override via <c>MK20_UPLOAD_THEME_NAME</c>). Requires <c>MK20_COM_PORT</c> - see
/// <see cref="HardwareConnection"/>.
/// </summary>
public class NavigationThemeUploadTests
{
    [Test]
    public async Task BuildAndUpload_ActivatesNavigationTheme()
    {
        string themeName = DeviceThemeNames.Resolve(DeviceThemeNames.Navigation);

        byte[] encoded = NavigationThemeBuilderTests.BuildNavigationTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {themeName}...");
        await client.UploadThemeAsync(themeName, encoded, TimeSpan.FromSeconds(30));

        TestContext.WriteLine(
            "Upload complete and theme activated. Page 1 is the hub: 'PAGE 1'/'PAGE 2' jump " +
            "absolutely, 'FOLDER' opens the folder page (bottom-right 'BACK' returns), and the " +
            "two ring pages page relatively via bottom-left/bottom-right with 'HOME' to jump back.");
    }
}
