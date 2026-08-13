using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// 6 pages, each a full 4x5 grid (20 keys - confirmed real MK20 main-screen grid size).
/// Bottom-left (row=3,col=0) = previous page, bottom-right (row=3,col=4) = next page on
/// every page (a ring: page 6's "next" goes back to page 1, page 1's "previous" goes to
/// page 6 - <c>PageSwitchAction</c> is always relative, not an absolute jump, so this needs
/// no special-casing). All other 18 keys per page (108 total) alternate between a numbered
/// icon and an animated GIF, each assigned a sequential letter of the alphabet (A-Z,
/// wrapping after Z back to A) - demonstrating <c>KeyItemBuilder.Icon</c> (static) and
/// <c>.AnimatedIcon</c> (animated) side-by-side across many keys. No hardware required for
/// this test. Formerly <c>Mk20Control.App</c>'s <c>BuildSixPageThemeFromScratch</c> /
/// <c>--build-6page-scratch</c>.
/// </summary>
public class SixPageThemeBuilderTests
{
    public const int Rows = 4, Cols = 5;
    public const int PageCount = 6;

    public static byte[] BuildSixPageTheme()
    {
        string gifAssetPath = TestPaths.GifFile("pop-cat.gif");
        bool hasGif = File.Exists(gifAssetPath);
        byte[]? gifBytes = hasGif ? File.ReadAllBytes(gifAssetPath) : null;

        // USB HID keycodes for 'A'-'Z' are 0x04-0x1D (4-29) in sequence.
        int letterIndex = 0;
        int iconCounter = 1; // cycles through icon_01..icon_40

        var builder = new ThemeBuilder();
        for (int p = 0; p < PageCount; p++)
        {
            builder.AddPage(page =>
            {
                page.SetCanvas(640, 656);
                for (int row = 0; row < Rows; row++)
                {
                    for (int col = 0; col < Cols; col++)
                    {
                        bool isBottomRow = row == Rows - 1;
                        if (isBottomRow && col == 0)
                        {
                            page.AddKey(row, col, key => key
                                // Confirmed via real theme files: page-switch keys reuse
                                // the fixed static system icon directly as their own "path".
                                .IconAssetPath("/static/icon/dark/PageSwitch.png")
                                .Action(KeyActions.PreviousPage()));
                            continue;
                        }
                        if (isBottomRow && col == Cols - 1)
                        {
                            page.AddKey(row, col, key => key
                                .IconAssetPath("/static/icon/dark/PageSwitch.png")
                                .Action(KeyActions.NextPage()));
                            continue;
                        }

                        char letter = (char)('A' + (letterIndex % 26));
                        int keycode = 0x04 + (letterIndex % 26);
                        letterIndex++;

                        bool useGif = hasGif && (letterIndex % 2 == 0);
                        int iconNum = ((iconCounter - 1) % 40) + 1;
                        iconCounter++;

                        page.AddKey(row, col, key =>
                        {
                            if (useGif)
                                key.AnimatedIcon($"popcat_{p}_{row}_{col}", gifBytes!);
                            else
                                key.Icon($"icon_{iconNum:D2}.png", File.ReadAllBytes(TestPaths.IconFile(iconNum)));
                            key.Action(KeyActions.Keyboard(keycode, letter.ToString()));
                        });
                    }
                }
            });
        }

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BuildSixPageTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildSixPageTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var allKeys = decoded.Pages.SelectMany(pg => pg.Items.OfType<KeyItem>()).ToList();
        int expectedKeysPerPage = Rows * Cols;

        Assert.That(decoded.Pages, Has.Count.EqualTo(PageCount));
        Assert.That(decoded.Pages.All(pg => pg.Items.OfType<KeyItem>().Count() == expectedKeysPerPage), Is.True);
        Assert.That(decoded.Pages.All(pg => pg.Encoder is not null), Is.True);
        Assert.That(allKeys.Count(k => k.Action is PageSwitchAction psa && psa.PageSwitchMode == 1), Is.EqualTo(PageCount));
        Assert.That(allKeys.Count(k => k.Action is PageSwitchAction psa && psa.PageSwitchMode == 2), Is.EqualTo(PageCount));
        Assert.That(allKeys.Count(k => k.Action is KeyboardAction), Is.EqualTo(PageCount * (expectedKeysPerPage - 2)));

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-6page-scratch-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}, {allKeys.Count} total key(s), {decoded.Assets.Count} asset(s)");
    }
}
