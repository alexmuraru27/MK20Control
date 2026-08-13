using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Matches the layout of a real 5-button theme plus a 6th key showing an animated GIF (a
/// real, pressable <see cref="KeyItem"/> using the confirmed animated-icon mechanism -
/// paths/frameDelays - NOT a type-114 <c>DynamicImageItem</c>, which is a separate
/// non-interactive decoration with no key behavior) and a 7th plain keyboard key - built
/// entirely from scratch via <see cref="ThemeBuilder"/>, using the confirmed real USB HID
/// digit keys consistently via <c>HidKey</c> instead of raw USB HID integers. No hardware
/// required for this test. Formerly <c>Mk20Control.App</c>'s
/// <c>BuildSevenKeyThemeFromScratch</c> / <c>--build-7key-scratch</c>.
/// </summary>
public class SevenKeyThemeBuilderTests
{
    private static readonly (int iconNum, HidKey key, string label)[] KeyboardKeys =
    {
        (16, HidKey.Digit1, "1"),
        (32, HidKey.Digit2, "2"),
        (28, HidKey.Digit3, "3"),
        (40, HidKey.Digit4, "4"),
        (8,  HidKey.Digit5, "5"),
        (20, HidKey.Digit9, "9"), // the label actually matches this key
        (21, HidKey.Digit0, "0"), // the 7th key
    };

    public static byte[] BuildSevenKeyTheme()
    {
        string gifAssetPath = TestPaths.GifFile("pop-cat.gif");
        bool hasGif = File.Exists(gifAssetPath);

        foreach (var (iconNum, _, _) in KeyboardKeys)
        {
            string iconFile = TestPaths.IconFile(iconNum);
            Assert.That(File.Exists(iconFile), Is.True, $"Missing icon file: {iconFile}.");
        }

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            for (int i = 0; i < KeyboardKeys.Length; i++)
            {
                var (iconNum, key, label) = KeyboardKeys[i];
                int row = i < 5 ? 0 : 1;
                int col = i < 5 ? i : i - 5;
                string iconFile = TestPaths.IconFile(iconNum);

                // The 6th key (index 5, row=1/col=1) is the animated cat key - a real,
                // pressable KeyItem whose icon is the multi-frame animation, assigned
                // Ctrl+Alt+Del via the strongly-typed KeyActions.KeyboardCombo API.
                bool isAnimatedKey = i == 5 && hasGif;
                page.AddKey(row, col, keyBuilder =>
                {
                    if (isAnimatedKey)
                        keyBuilder.AnimatedIcon("pop-cat", File.ReadAllBytes(gifAssetPath));
                    else
                        keyBuilder.Icon($"icon_{iconNum:D2}.png", File.ReadAllBytes(iconFile));
                    keyBuilder.Action(isAnimatedKey
                        ? KeyActions.KeyboardCombo(KeyModifiers.LeftCtrl | KeyModifiers.LeftAlt, HidKey.Delete, "L Ctrl L Alt Del")
                        : KeyActions.Keyboard(key, label));
                });
            }
        });

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BuildSevenKeyTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildSevenKeyTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var keys = decoded.Pages[0].Items.OfType<KeyItem>().ToList();

        Assert.That(decoded.Pages, Has.Count.EqualTo(1));
        Assert.That(keys, Has.Count.EqualTo(KeyboardKeys.Length));
        Assert.That(keys.All(k => k.Action is KeyboardAction), Is.True);
        Assert.That(decoded.Pages[0].Encoder, Is.Not.Null);

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-7key-scratch-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}, {decoded.Assets.Count} asset(s)");
    }
}
