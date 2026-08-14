using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Same 5 title/opacity keys as <see cref="TitleOpacityDemoTests"/>, plus:
/// <list type="bullet">
/// <item>a real static image, resized to fill the confirmed real 640x512 main-screen
/// background area, embedded as a type-114 <c>DynamicImageItem</c> via
/// <c>DynamicImageItemBuilder.MainScreenBackground</c> - NOT a type-100
/// <c>BackgroundItem</c>. Confirmed via a genuine ScreenKeyWindows-saved reference file
/// (see PROTOCOL_WAVESHARE_MK20.md §6.5/§10 Item #14).</item>
/// <item>a real GIF, resized to fill the confirmed real 428x142 secondary (2.8") screen
/// area, embedded via <c>DynamicImageItemBuilder.SecondaryScreenBackground</c> - a type-114
/// item at the fixed x=106,y=0,w=428,h=142 position with <c>"backgroundType":"secondary"</c>,
/// confirmed via <c>defaultTheme.Theme</c>.</item>
/// </list>
/// No hardware required for this test. Formerly <c>Mk20Control.App</c>'s
/// <c>BuildTitleOpacityDemoWithBackgroundsFromScratch</c> / <c>--build-title-opacity-backgrounds-demo</c>.
/// </summary>
public class TitleOpacityBackgroundsDemoTests
{
    public static byte[] BuildTitleOpacityBackgroundsDemoTheme()
    {
        string mainBgImagePath = TestPaths.BackgroundFile("Racing_Setup_Cheatsheet.jpg");
        string secondaryBgGifPath = TestPaths.GifFile("pop-cat.gif");
        Assert.That(File.Exists(mainBgImagePath), Is.True, $"Missing image: {mainBgImagePath}");
        Assert.That(File.Exists(secondaryBgGifPath), Is.True, $"Missing GIF: {secondaryBgGifPath}");

        foreach (var (iconNum, _, _, _, _, _, _) in TitleOpacityDemoTests.Keys)
            Assert.That(File.Exists(TestPaths.IconFile(iconNum)), Is.True, $"Missing icon file: icon_{iconNum:D2}.png.");

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            page.AddDynamicImage(img => img.MainScreenBackgroundAutoFit("Racing_Setup_Cheatsheet.jpg", File.ReadAllBytes(mainBgImagePath)));
            page.AddDynamicImage(img => img.SecondaryScreenBackgroundAutoFit("popcat_secondary.gif", File.ReadAllBytes(secondaryBgGifPath)));

            for (int i = 0; i < TitleOpacityDemoTests.Keys.Length; i++)
            {
                var (iconNum, key, label, title, opacity, alignment, colorHex) = TitleOpacityDemoTests.Keys[i];
                string iconFile = TestPaths.IconFile(iconNum);
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
    public void BuildTitleOpacityBackgroundsDemoTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildTitleOpacityBackgroundsDemoTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var keys = decoded.Pages[0].Items.OfType<KeyItem>().ToList();
        var mainBg = decoded.Pages[0].Items.OfType<Mk20Control.Protocol.Theme.Items.DynamicImageItem>().FirstOrDefault(d => d.BackgroundType == "main");
        var secondaryBg = decoded.Pages[0].Items.OfType<Mk20Control.Protocol.Theme.Items.DynamicImageItem>().FirstOrDefault(d => d.BackgroundType == "secondary");

        Assert.That(decoded.Pages, Has.Count.EqualTo(1));
        Assert.That(keys, Has.Count.EqualTo(TitleOpacityDemoTests.Keys.Length));
        Assert.That(keys.All(k => k.Action is KeyboardAction), Is.True);
        Assert.That(decoded.Pages[0].Encoder, Is.Not.Null);

        Assert.That(mainBg, Is.Not.Null, "Main-screen background item missing.");
        Assert.That(mainBg!.X, Is.EqualTo(0));
        Assert.That(mainBg.Y, Is.EqualTo(144));
        Assert.That(mainBg.Width, Is.EqualTo(640));
        Assert.That(mainBg.Height, Is.EqualTo(512));

        Assert.That(secondaryBg, Is.Not.Null, "Secondary-screen background item missing.");
        Assert.That(secondaryBg!.X, Is.EqualTo(106));
        Assert.That(secondaryBg.Y, Is.EqualTo(0));
        Assert.That(secondaryBg.Width, Is.EqualTo(428));
        Assert.That(secondaryBg.Height, Is.EqualTo(142));

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-title-opacity-backgrounds-demo-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}, {decoded.Assets.Count} asset(s)");
    }
}
