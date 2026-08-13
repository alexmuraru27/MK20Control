using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Building.Widgets;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// SANDBOX-derived theme (not part of the confirmed API surface) - fills the 428x142
/// secondary screen with a cat GIF background and overlays one live-data-bound item of
/// every gauge/text type (mirrors the confirmed real layout observed in
/// ScreenKeyWindows_v1_1's SecondaryScreen/1/1.theme: bars/text at x~20-90 and x~326-411,
/// GIF at x=106..534). Main screen carries a single plain key so the page is valid.
/// No hardware required for this test; see
/// <c>HardwareTests.SecondaryScreenGaugesOverlayTests</c> for the upload+telemetry-pump
/// variant. Formerly <c>Mk20Control.App</c>'s <c>BuildSecondaryGaugesGifSandboxTheme</c> /
/// <c>--build-gauges-scratch</c>.
/// </summary>
public class SecondaryScreenGaugesOverlayThemeTests
{
    public static byte[] BuildTheme()
    {
        string secondaryBgGifPath = TestPaths.GifFile("pop-cat.gif");
        Assert.That(File.Exists(secondaryBgGifPath), Is.True, $"Missing GIF: {secondaryBgGifPath}");

        string iconFile = TestPaths.IconFile(1);
        Assert.That(File.Exists(iconFile), Is.True, $"Missing icon file: {iconFile}.");

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);

            page.AddKey(0, 0, keyBuilder =>
            {
                keyBuilder.Icon("icon_01.png", File.ReadAllBytes(iconFile));
                keyBuilder.Action(KeyActions.Keyboard(HidKey.Digit1, "1"));
            });

            page.AddDynamicImage(img => img.SecondaryScreenBackgroundAutoFit("popcat_secondary.gif", File.ReadAllBytes(secondaryBgGifPath)));

            // Left margin (x ~20-90): ProgressBar bound to "CPU Usage" + its readout text.
            page.AddProgressBar(pb => pb.At(114, 8, 85, 22, z: 2).BoundTo("CPU Usage", 0, 100)
                .Colors("r=0,g=170,b=255,a=220", "r=255,g=255,b=255,a=140", "r=0,g=0,b=0,a=180", 2, 6));
            page.AddText(t => t.At(120, 12, z: 3).BoundTo("CPU Usage").Font("Microsoft YaHei,10,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=255"));

            // Left margin, second row: LinearGauge bound to "RAM Usage" + readout text.
            page.AddLinearGauge(lg => lg.At(114, 34, 85, 12, z: 2).BoundTo("RAM Usage", 0, 100)
                .Colors("r=0,g=220,b=120,a=220", "r=255,g=255,b=255,a=140", "r=0,g=0,b=0,a=180", 2));
            page.AddText(t => t.At(120, 46, z: 3).BoundTo("RAM Usage").Font("Microsoft YaHei,9,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=255"));

            // Right margin: RadialGauge bound to "GPU Usage" (dial arc).
            page.AddRadialGauge(rg => rg.At(360, 8, z: 2, scale: 0.35).BoundTo("GPU Usage", 0, 100)
                .Gradient("r=0,g=170,b=255,a=255", "r=255,g=200,b=0,a=255", "r=255,g=0,b=0,a=255"));
            page.AddText(t => t.At(376, 30, z: 3).BoundTo("GPU Usage").Font("Microsoft YaHei,9,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=255"));

            // Right margin, second row: static label text + digital clock fields (hour:minute).
            page.AddText(t => t.At(360, 60).Text("Time").Font("Microsoft YaHei,9,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=200"));
            page.AddDigitalClockField(c => c.At(340, 78, 20, 16, z: 2).Field("hour").Colors(
                "r=255,g=255,b=255,a=255", "r=0,g=0,b=0,a=0", "r=0,g=0,b=0,a=0"));
            page.AddDigitalClockField(c => c.At(362, 78, 20, 16, z: 2).Field("minute").Colors(
                "r=255,g=255,b=255,a=255", "r=0,g=0,b=0,a=0", "r=0,g=0,b=0,a=0"));
        });

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BuildTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var items = decoded.Pages[0].Items;
        var secondaryBg = items.OfType<DynamicImageItem>().FirstOrDefault(d => d.BackgroundType == "secondary");

        Assert.That(decoded.Pages, Has.Count.EqualTo(1));
        Assert.That(decoded.Pages[0].Encoder, Is.Not.Null);
        Assert.That(secondaryBg, Is.Not.Null);
        Assert.That(items.OfType<ProgressBarItem>().Any(), Is.True);
        Assert.That(items.OfType<LinearGaugeItem>().Any(), Is.True);
        Assert.That(items.OfType<RadialGaugeItem>().Any(), Is.True);
        Assert.That(items.OfType<DigitalClockItem>().Count(), Is.EqualTo(2));
        Assert.That(items.OfType<TextItem>().Count(), Is.GreaterThanOrEqualTo(3));

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-gauges-scratch-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}, {decoded.Assets.Count} asset(s)");
    }
}
