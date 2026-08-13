using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Loads a local <c>.Theme</c> file, adds one new key via <see cref="ThemeEditorAddKeyTests"/>'s
/// <c>AddKeyToTheme</c> helper, and uploads the edited result to a real device. Set
/// <c>MK20_EDIT_LOCAL_THEME_PATH</c> to an existing local <c>.Theme</c> file,
/// <c>MK20_EDIT_ROW</c>/<c>MK20_EDIT_COL</c> to the new key's grid position,
/// <c>MK20_EDIT_ICON_FILE</c> to an icon file name under <c>assets/icons</c>,
/// <c>MK20_EDIT_KEYCODE</c> to a USB HID keycode (decimal), <c>MK20_EDIT_KEY_LABEL</c> to
/// the key's label, and <c>MK20_UPLOAD_DEVICE_PATH</c> to the destination device-side path
/// - the test is skipped if any are missing. Requires <c>MK20_COM_PORT</c> - see
/// <see cref="HardwareConnection"/>. Formerly <c>Mk20Control.App</c> menu option 16.
/// </summary>
public class ThemeEditorAddKeyAndUploadTests
{
    [Test]
    public async Task AddKeyAndUpload_ActivatesEditedTheme()
    {
        string? localPath = Environment.GetEnvironmentVariable("MK20_EDIT_LOCAL_THEME_PATH");
        string? devicePath = Environment.GetEnvironmentVariable("MK20_UPLOAD_DEVICE_PATH");
        string? iconFileName = Environment.GetEnvironmentVariable("MK20_EDIT_ICON_FILE");
        string? keyLabel = Environment.GetEnvironmentVariable("MK20_EDIT_KEY_LABEL");
        bool haveRow = int.TryParse(Environment.GetEnvironmentVariable("MK20_EDIT_ROW"), out int row);
        bool haveCol = int.TryParse(Environment.GetEnvironmentVariable("MK20_EDIT_COL"), out int col);
        bool haveKeycode = int.TryParse(Environment.GetEnvironmentVariable("MK20_EDIT_KEYCODE"), out int keycode);

        if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(devicePath) ||
            string.IsNullOrWhiteSpace(iconFileName) || string.IsNullOrWhiteSpace(keyLabel) ||
            !haveRow || !haveCol || !haveKeycode)
        {
            Assert.Ignore("Set MK20_EDIT_LOCAL_THEME_PATH, MK20_EDIT_ROW, MK20_EDIT_COL, " +
                "MK20_EDIT_ICON_FILE, MK20_EDIT_KEYCODE, MK20_EDIT_KEY_LABEL, and " +
                "MK20_UPLOAD_DEVICE_PATH to run this test.");
        }
        if (!File.Exists(localPath))
            Assert.Fail($"File not found: {localPath}");

        byte[] original = File.ReadAllBytes(localPath!);
        byte[] edited = ThemeEditorAddKeyTests.AddKeyToTheme(original, row, col, iconFileName!, keycode, keyLabel!);

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {edited.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath!, edited, TimeSpan.FromSeconds(30));

        TestContext.WriteLine("Upload complete and theme activated.");
    }
}
