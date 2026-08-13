using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Builds the simplest possible one-key theme entirely by hand (no <c>ThemeBuilder</c>),
/// confirming <see cref="ThemeFileCodec"/>'s low-level encode/decode round-trip. No hardware
/// required. Formerly <c>Mk20Control.App</c> menu option 11 / <c>BuildDemoTheme</c>.
/// </summary>
public class DemoThemeBuilderTests
{
    /// <summary>Builds a single key (icon + 'A' keystroke) using the raw <see cref="ThemeFile"/> model directly, and verifies it decodes back correctly.</summary>
    public static byte[] BuildDemoTheme()
    {
        var iconFiles = Directory.Exists(TestPaths.IconsDir) ? Directory.GetFiles(TestPaths.IconsDir, "*.png") : Array.Empty<string>();
        Assert.That(iconFiles, Is.Not.Empty, $"No icons found in {TestPaths.IconsDir}. Run tools\\AssetGenerator first.");

        byte[] iconBytes = File.ReadAllBytes(iconFiles[0]);
        const string assetPath = "/image/demo/icon1.png";

        var keyAction = new KeyboardAction
        {
            RawType = "keyboard",
            Description = "Keyboard",
            KeyLabel = "A",
            Keycode = 4, // USB HID usage 0x04 = 'A', confirmed against real captured remaps
            RawFields = new Dictionary<string, TaggedValue>(),
        };

        var keyItem = new KeyItem
        {
            RawTypeCode = "115",
            Id = "1",
            X = 0, Y = 0, Z = 1, Width = 128, Height = 128, Rotate = 0, Scale = 1, IsLocked = true,
            Row = 0,
            Column = 0,
            IconAssetPath = assetPath,
            Action = keyAction,
            RawJson = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
        };

        var page = new ThemePage
        {
            PageName = Guid.NewGuid().ToString(),
            Canvas = new ThemeCanvas { Width = 640, Height = 656, IsFlipped = false, IsRotated = false, ShowUnit = true },
            Items = new[] { (ThemeItem)keyItem },
        };

        var theme = new ThemeFile
        {
            Language = 0,
            KeyMacroValue = Array.Empty<byte>(),
            KeyMacro = null,
            CurrentPageId = page.PageName!,
            LayoutVersion = "V3.0",
            Pages = new[] { page },
            Assets = new[] { new ThemeAsset { Path = assetPath, Data = iconBytes } },
        };

        return ThemeFileCodec.Encode(theme);
    }

    [Test]
    public void BuildDemoTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildDemoTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var key = decoded.Pages[0].Items.OfType<KeyItem>().FirstOrDefault();

        Assert.That(decoded.Pages, Has.Count.EqualTo(1));
        Assert.That(key, Is.Not.Null);
        Assert.That(key!.Row, Is.EqualTo(0));
        Assert.That(key.Column, Is.EqualTo(0));
        Assert.That(key.Action, Is.InstanceOf<KeyboardAction>());
        Assert.That(((KeyboardAction)key.Action!).Keycode, Is.EqualTo(4));
        Assert.That(decoded.Assets, Has.Count.EqualTo(1));

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-demo-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}");
    }
}
