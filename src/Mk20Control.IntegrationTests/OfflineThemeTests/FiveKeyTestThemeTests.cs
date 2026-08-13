using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Builds a 5-key test theme (icons 01-05 on the top row, each typing digits '1'-'5', plus a
/// main-screen background) entirely by hand, confirming the raw <see cref="ThemeFile"/>
/// model round-trips correctly. No hardware required.
/// Formerly <c>Mk20Control.App</c>'s <c>BuildFiveKeyTestTheme</c> / <c>--build-test5-local</c>.
/// </summary>
public class FiveKeyTestThemeTests
{
    /// <summary>
    /// USB HID keyboard usage IDs for the top-row digit keys: '1'=0x1E(30) .. '5'=0x22(34) -
    /// the same standard USB HID keyboard usage table starting at 0x1E for '1' (0x04 = 'A').
    /// </summary>
    public static byte[] BuildFiveKeyTestTheme()
    {
        var items = new List<ThemeItem>();
        var assets = new List<ThemeAsset>();

        // Every real theme examined has a main-screen background item (type 100) covering
        // the 640x512 key-grid region - include one here too so the on-device renderer has
        // the full structure it expects, rather than only bare key items.
        string backgroundFile = TestPaths.BackgroundFile("gradient_main_screen_640x512.png");
        Assert.That(File.Exists(backgroundFile), Is.True, $"Missing background file: {backgroundFile}. Run tools\\AssetGenerator first.");

        const string backgroundAssetPath = "/image/test5/background.png";
        assets.Add(new ThemeAsset { Path = backgroundAssetPath, Data = File.ReadAllBytes(backgroundFile) });
        items.Add(new BackgroundItem
        {
            RawTypeCode = "100",
            Id = "1",
            X = 0, Y = 144, Z = -2, Width = 640, Height = 512, Rotate = 0, Scale = 1, IsLocked = true,
            RawSurface = "main",
            Surface = BackgroundSurface.Main,
            AssetPath = backgroundAssetPath,
            RawJson = System.Text.Json.JsonDocument.Parse("""{"maxWidth":"640","maxHeight":"512"}""").RootElement.Clone(),
        });

        for (int i = 1; i <= 5; i++)
        {
            string iconFile = TestPaths.IconFile(i);
            Assert.That(File.Exists(iconFile), Is.True, $"Missing icon file: {iconFile}. Run tools\\AssetGenerator first.");

            string assetPath = $"/image/test5/icon_{i:D2}.png";
            assets.Add(new ThemeAsset { Path = assetPath, Data = File.ReadAllBytes(iconFile) });

            var action = new KeyboardAction
            {
                RawType = "keyboard",
                Description = "Keyboard",
                KeyLabel = i.ToString(),
                Keycode = 0x1D + i, // '1'=0x1E .. '5'=0x22
                RawFields = new Dictionary<string, TaggedValue>(),
            };

            // Real key items (type 115) do NOT carry "w"/"h" fields - instead they use
            // "maxWidth"/"maxHeight" (canvas cell bounds) plus "scaledWidthTo"/
            // "scaledHeightTo" (rendered icon size), alongside "opacity"/"paths"/
            // "soundFile"/"title"/"titleParam", which are always present.
            var keyRawJson = System.Text.Json.JsonDocument.Parse($$"""
                {
                  "maxWidth": "640",
                  "maxHeight": "656",
                  "opacity": "100",
                  "paths": "",
                  "scaledWidthTo": "128",
                  "scaledHeightTo": "128",
                  "soundFile": "",
                  "title": "",
                  "titleParam": "{\"FontFamily\":\"Microsoft YaHei\",\"FontSize\":24,\"FontStyle\":\"\",\"FontUnderline\":false,\"ShowImage\":true,\"ShowTitle\":true,\"TitleAlignment\":\"bottom\",\"TitleColor\":\"#ffffff\"}"
                }
                """).RootElement.Clone();

            items.Add(new KeyItem
            {
                RawTypeCode = "115",
                Id = (i + 1).ToString(),
                X = (i - 1) * 128, Y = 144, Z = 1, Rotate = 0, Scale = 1, IsLocked = true,
                Row = 0,
                Column = i - 1,
                IconAssetPath = assetPath,
                Action = action,
                RawJson = keyRawJson,
            });
        }

        var page = new ThemePage
        {
            PageName = Guid.NewGuid().ToString(),
            Canvas = new ThemeCanvas { Width = 640, Height = 656, IsFlipped = false, IsRotated = false, ShowUnit = true },
            Items = items,
        };

        var theme = new ThemeFile
        {
            Language = 0,
            KeyMacroValue = Array.Empty<byte>(),
            KeyMacro = null,
            CurrentPageId = page.PageName!,
            LayoutVersion = "V3.0",
            Pages = new[] { page },
            Assets = assets,
        };

        return ThemeFileCodec.Encode(theme);
    }

    [Test]
    public void BuildFiveKeyTestTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildFiveKeyTestTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var keys = decoded.Pages[0].Items.OfType<KeyItem>().OrderBy(k => k.Column).ToList();

        Assert.That(decoded.Pages, Has.Count.EqualTo(1));
        Assert.That(decoded.Pages[0].Items.OfType<BackgroundItem>().Any(), Is.True);
        Assert.That(keys, Has.Count.EqualTo(5));
        Assert.That(decoded.Assets, Has.Count.EqualTo(6)); // 1 background + 5 icons
        for (int i = 0; i < keys.Count; i++)
        {
            Assert.That(keys[i].Row, Is.EqualTo(0));
            Assert.That(keys[i].Column, Is.EqualTo(i));
            Assert.That(((KeyboardAction)keys[i].Action!).Keycode, Is.EqualTo(0x1E + i));
        }

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-test5-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}");
    }
}
