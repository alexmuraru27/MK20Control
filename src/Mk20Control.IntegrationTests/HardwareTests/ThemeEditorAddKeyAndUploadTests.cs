using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Loads a local <c>.Theme</c> file, adds one new key via <see cref="ThemeEditorAddKeyTests"/>'s
/// <c>AddKeyToTheme</c> helper, and uploads the edited result to a real device. Fully
/// self-contained by default: if <c>MK20_EDIT_LOCAL_THEME_PATH</c> isn't set, edits the
/// theme built by <see cref="FiveKeyTestThemeTests.BuildFiveKeyTestTheme"/> instead of
/// requiring an existing file on disk; uploads to the fixed self-contained path
/// <see cref="DeviceThemeNames.ThemeEditorAddKey"/> unless overridden via
/// <c>MK20_UPLOAD_THEME_NAME</c>. Optional overrides: <c>MK20_EDIT_ROW</c>/
/// <c>MK20_EDIT_COL</c> (default 1,0 - free in the base 5-key theme),
/// <c>MK20_EDIT_ICON_FILE</c> (default "icon_07.png"), <c>MK20_EDIT_KEYCODE</c> (default
/// 0x24 = '7'), <c>MK20_EDIT_KEY_LABEL</c> (default "7"). Requires <c>MK20_COM_PORT</c> -
/// see <see cref="HardwareConnection"/>. Formerly <c>Mk20Control.App</c> menu option 16.
/// </summary>
public class ThemeEditorAddKeyAndUploadTests
{
    [Test]
    public async Task AddKeyAndUpload_ActivatesEditedTheme()
    {
        string? localPath = Environment.GetEnvironmentVariable("MK20_EDIT_LOCAL_THEME_PATH");
        string themeName = DeviceThemeNames.Resolve(DeviceThemeNames.ThemeEditorAddKey);
        string iconFileName = Environment.GetEnvironmentVariable("MK20_EDIT_ICON_FILE") is { Length: > 0 } icon ? icon : "icon_07.png";
        string keyLabel = Environment.GetEnvironmentVariable("MK20_EDIT_KEY_LABEL") is { Length: > 0 } label ? label : "7";
        int row = int.TryParse(Environment.GetEnvironmentVariable("MK20_EDIT_ROW"), out int r) ? r : 1;
        int col = int.TryParse(Environment.GetEnvironmentVariable("MK20_EDIT_COL"), out int c) ? c : 0;
        int keycode = int.TryParse(Environment.GetEnvironmentVariable("MK20_EDIT_KEYCODE"), out int kc) ? kc : 0x24; // '7'

        byte[] original;
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            if (!File.Exists(localPath)) Assert.Fail($"File not found: {localPath}");
            original = File.ReadAllBytes(localPath);
        }
        else
        {
            TestContext.WriteLine("MK20_EDIT_LOCAL_THEME_PATH not set - editing the self-contained 5-key test theme instead.");
            original = FiveKeyTestThemeTests.BuildFiveKeyTestTheme();
        }

        byte[] edited = ThemeEditorAddKeyTests.AddKeyToTheme(original, row, col, iconFileName, keycode, keyLabel);

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {edited.Length} bytes to {themeName}...");
        await client.UploadThemeAsync(themeName, edited, TimeSpan.FromSeconds(30));

        TestContext.WriteLine("Upload complete and theme activated.");
    }
}
