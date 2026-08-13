using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Loads an existing local <c>.Theme</c> file (here, the theme built by
/// <see cref="FiveKeyTestThemeTests"/>, so the test is self-contained) and adds one new key
/// at a free grid position using <see cref="ThemeEditor"/>, verifying the edit round-trips
/// locally - demonstrating the "edit an existing/real theme" workflow distinct from building
/// a brand-new one via <see cref="ThemeBuilder"/>. No hardware required. Formerly
/// <c>Mk20Control.App</c>'s <c>AddKeyToTheme</c> / menu option 16 / <c>--add-key-local</c>.
/// </summary>
public class ThemeEditorAddKeyTests
{
    public static byte[] AddKeyToTheme(byte[] originalThemeBytes, int row, int col, string iconFileName, int keycode, string keyLabel)
    {
        var original = ThemeFileCodec.Decode(originalThemeBytes);
        var editor = new ThemeEditor(original);
        if (editor.Page(0).FindKey(row, col) is not null)
            throw new InvalidOperationException($"A key already exists at row={row}, col={col}; refusing to overwrite it silently.");

        string iconFile = Path.Combine(TestPaths.IconsDir, iconFileName);
        editor.Page(0).AddKey(row, col, key => key
            .Icon(iconFileName, File.ReadAllBytes(iconFile))
            .Action(KeyActions.Keyboard(keycode, keyLabel)));

        return ThemeFileCodec.Encode(editor.Save());
    }

    [Test]
    public void AddKeyToTheme_RoundTripsCorrectly()
    {
        byte[] original = FiveKeyTestThemeTests.BuildFiveKeyTestTheme();
        var originalDecoded = ThemeFileCodec.Decode(original);

        const int row = 1, col = 0, keycode = 0x24; // '7'
        byte[] edited = AddKeyToTheme(original, row, col, "icon_07.png", keycode, "7");

        var decoded = ThemeFileCodec.Decode(edited);
        var newKey = decoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault(k => k.Row == row && k.Column == col);

        Assert.That(newKey, Is.Not.Null);
        Assert.That(newKey!.Action, Is.InstanceOf<KeyboardAction>());
        Assert.That(((KeyboardAction)newKey.Action!).Keycode, Is.EqualTo(keycode));
        Assert.That(decoded.Assets, Has.Count.EqualTo(originalDecoded.Assets.Count + 1));

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-edited-theme.Theme");
        File.WriteAllBytes(outPath, edited);
        TestContext.WriteLine($"Wrote {edited.Length} bytes to {outPath} ({originalDecoded.Assets.Count} -> {decoded.Assets.Count} assets)");
    }

    [Test]
    public void AddKeyToTheme_RefusesToOverwriteExistingKey()
    {
        byte[] original = FiveKeyTestThemeTests.BuildFiveKeyTestTheme();

        // Row 0, column 0 is already occupied by the five-key test theme's first key.
        Assert.Throws<InvalidOperationException>(() => AddKeyToTheme(original, row: 0, col: 0, "icon_07.png", 0x24, "7"));
    }
}
