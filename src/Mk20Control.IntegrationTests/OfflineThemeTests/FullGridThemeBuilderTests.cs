using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Builds a full 20-key grid theme (4 rows x 5 columns - the confirmed real MK20
/// main-screen layout) using icons 01-20 and a real background, entirely through the
/// <c>Mk20Control.Protocol.Theme.Building</c> fluent API (no manual JSON/RawJson
/// construction) - demonstrates <see cref="ThemeBuilder"/> as the primary "set a picture on
/// a button and make it do X" entry point. Each key emits a distinct USB HID keyboard
/// keycode (digits 1-9,0 then letters A-J) so every key's effect is easy to verify by
/// typing into a text box while the theme is active. No hardware required for this test;
/// see <c>HardwareTests.FullGridThemeUploadTests</c> for the upload variant.
/// Formerly <c>Mk20Control.App</c>'s <c>BuildFullGridTheme</c> / <c>--build-fullgrid-local</c>.
/// </summary>
public class FullGridThemeBuilderTests
{
    public const int Rows = 4, Cols = 5; // 20 keys total, matching the confirmed real MK20 grid

    public static byte[] BuildFullGridTheme()
    {
        string backgroundFile = TestPaths.BackgroundFile("color_bars_main_screen_640x512.png");
        Assert.That(File.Exists(backgroundFile), Is.True, $"Missing background file: {backgroundFile}. Run tools\\AssetGenerator first.");

        // USB HID keycodes: '1'-'9'=0x1E-0x26, '0'=0x27, then 'A'-'J'=0x04-0x0D.
        var keycodes = new List<(int code, string label)>();
        for (int d = 1; d <= 9; d++) keycodes.Add((0x1E + (d - 1), d.ToString()));
        keycodes.Add((0x27, "0"));
        for (int c = 0; c < 10; c++) keycodes.Add((0x04 + c, ((char)('A' + c)).ToString()));

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            page.AddBackground(bg => bg.MainScreen("color_bars_main_screen_640x512.png", File.ReadAllBytes(backgroundFile)));

            int keyIndex = 0;
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    int iconNum = keyIndex + 1; // icon_01..icon_20
                    string iconFile = TestPaths.IconFile(iconNum);
                    Assert.That(File.Exists(iconFile), Is.True, $"Missing icon file: {iconFile}. Run tools\\AssetGenerator first.");

                    var (code, label) = keycodes[keyIndex];
                    page.AddKey(row, col, key => key
                        .Icon($"icon_{iconNum:D2}.png", File.ReadAllBytes(iconFile))
                        .Action(KeyActions.Keyboard(code, label)));
                    keyIndex++;
                }
            }
        });

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BuildFullGridTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildFullGridTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var keys = decoded.Pages[0].Items.OfType<KeyItem>().ToList();

        Assert.That(decoded.Pages, Has.Count.EqualTo(1));
        Assert.That(decoded.Pages[0].Items.OfType<BackgroundItem>().Any(), Is.True);
        Assert.That(keys, Has.Count.EqualTo(Rows * Cols));
        Assert.That(decoded.Assets, Has.Count.EqualTo(Rows * Cols + 1)); // 20 icons + 1 background

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-fullgrid-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}");
    }
}
