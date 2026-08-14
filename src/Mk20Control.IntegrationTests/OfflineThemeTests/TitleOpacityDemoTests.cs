using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Demonstrates the confirmed real "text over a button" mechanism
/// (<c>KeyItemBuilder.Title</c>/<c>.Opacity</c>/<c>.TitleStyle</c>), cross-checked against a
/// real ScreenKeyWindows capture (<c>tools/Captures/capture19_text_over_buttons_and_txtinput.pcapng</c>)
/// where the vendor editor's "title" + "transparency" controls were shown to set exactly
/// these same two JSON fields ("title", "opacity") on a key item - not a separate overlay.
/// 5 keys across the top row: a plain keyboard key (control), 3 keys each showing a labeled
/// title over a progressively more transparent icon, and a custom-styled title. No hardware
/// required. Formerly <c>Mk20Control.App</c>'s <c>BuildTitleOpacityDemoThemeFromScratch</c> /
/// <c>--build-title-opacity-demo</c>.
/// </summary>
public class TitleOpacityDemoTests
{
    public static readonly (int iconNum, HidKey key, string label, string title, int opacity, string? alignment, string? colorHex)[] Keys =
    {
        (1,  HidKey.Digit1, "1", "",           100, null,   null),      // plain control key, no title/opacity change
        (2,  HidKey.Digit2, "2", "Opaque",     100, null,   null),      // title shown, icon fully opaque
        (3,  HidKey.Digit3, "3", "Semi",        50, null,   null),      // title shown, icon at 50% opacity
        (4,  HidKey.Digit4, "4", "Faint",       15, null,   null),      // title shown, icon at 15% opacity (matches the real capture's value)
        (5,  HidKey.Digit5, "5", "Styled",      15, "top",  "#ff0000"), // 15% opacity + custom red, top-aligned title
    };

    public static byte[] BuildTitleOpacityDemoTheme()
    {
        foreach (var (iconNum, _, _, _, _, _, _) in Keys)
            Assert.That(File.Exists(Support.TestPaths.IconFile(iconNum)), Is.True, $"Missing icon file: icon_{iconNum:D2}.png.");

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            for (int i = 0; i < Keys.Length; i++)
            {
                var (iconNum, key, label, title, opacity, alignment, colorHex) = Keys[i];
                string iconFile = Support.TestPaths.IconFile(iconNum);
                page.AddKey(0, i, keyBuilder =>
                {
                    keyBuilder.Icon($"icon_{iconNum:D2}.png", File.ReadAllBytes(iconFile));
                    keyBuilder.Title(title);
                    keyBuilder.Opacity(opacity);
                    if (alignment is not null || colorHex is not null)
                        keyBuilder.TitleStyle(alignment: alignment, color: colorHex is null ? (ThemeColor?)null : ThemeColor.Parse(colorHex));
                    keyBuilder.Action(KeyActions.Keyboard(key, label));
                });
            }
        });

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BuildTitleOpacityDemoTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildTitleOpacityDemoTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var keys = decoded.Pages[0].Items.OfType<KeyItem>().ToList();

        Assert.That(decoded.Pages, Has.Count.EqualTo(1));
        Assert.That(keys, Has.Count.EqualTo(Keys.Length));
        Assert.That(keys.All(k => k.Action is KeyboardAction), Is.True);
        Assert.That(decoded.Pages[0].Encoder, Is.Not.Null);
        Assert.That(keys.Any(k => k.RawJson.TryGetProperty("title", out var t) && t.GetString() == "Faint"), Is.True);
        Assert.That(keys.Any(k => k.RawJson.TryGetProperty("opacity", out var o) && o.GetString() == "15"), Is.True);

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-title-opacity-demo-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}");
    }
}
