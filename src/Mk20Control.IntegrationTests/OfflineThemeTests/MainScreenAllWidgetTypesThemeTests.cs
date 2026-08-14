using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Building.Widgets;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// SANDBOX-derived theme (not part of the confirmed API surface) - fills the 640x656 main
/// screen with ONE instance of every distinct widget type, no keys/buttons at all, each
/// data-bound widget bound to its own test channel ("test1", "test2", ...) so pushing
/// varied values per channel visibly animates every widget independently. Covers all
/// widget types this library supports: ProgressBar, LinearGauge, RadialGauge,
/// CircularGauge, SegmentedCircularGauge, LightShadowGauge, Text, MultilineText,
/// ShadowText, and DigitalClock (hour/minute/second - fed by the host, not the device's own
/// RTC, so it ticks only while telemetry is being pushed - see
/// <c>HardwareTests.MainScreenAllWidgetTypesTests</c> for the live-pump variant).
/// No hardware required for this test. Formerly <c>Mk20Control.App</c>'s
/// <c>BuildMainScreenWidgetTestTheme</c> / <c>--build-widget-test-scratch</c>.
/// </summary>
public class MainScreenAllWidgetTypesThemeTests
{
    public static byte[] BuildTheme()
    {
        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);

            // Row 1 (y=20): ProgressBar / LinearGauge / RadialGauge - test1/test2/test3.
            page.AddProgressBar(pb => pb.At(20, 20, 150, 30).BoundTo("test1", 0, 100)
                .Colors("r=0,g=170,b=255,a=220", "r=255,g=255,b=255,a=140", "r=0,g=0,b=0,a=180", 2, 6));
            page.AddText(t => t.At(20, 55).BoundTo("test1").Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=255"));

            page.AddLinearGauge(lg => lg.At(220, 20, 150, 20).BoundTo("test2", 0, 100)
                .Colors("r=0,g=220,b=120,a=220", "r=255,g=255,b=255,a=140", "r=0,g=0,b=0,a=180", 2));
            page.AddText(t => t.At(220, 45).BoundTo("test2").Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=255"));

            page.AddRadialGauge(rg => rg.At(420, 10, z: 1, scale: 0.4).BoundTo("test3", 0, 100)
                .Gradient("r=0,g=170,b=255,a=255", "r=255,g=200,b=0,a=255", "r=255,g=0,b=0,a=255"));
            page.AddText(t => t.At(430, 90).BoundTo("test3").Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=255"));

            // Row 2 (y=200): CircularGauge / SegmentedCircularGauge / LightShadowGauge - test4/5/6.
            page.AddCircularGauge(g => g.At(50, 200).BoundTo("test4", 0, 100)
                .Colors("r=0,g=255,b=0,a=255", "r=60,g=60,b=60,a=255").Geometry(20, 80));
            page.AddText(t => t.At(35, 300).BoundTo("test4").Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=255"));

            page.AddSegmentedCircularGauge(g => g.At(260, 200).BoundTo("test5", 0, 100)
                .Colors("r=255,g=170,b=0,a=255", "r=60,g=60,b=60,a=255").Geometry(20, 80));
            page.AddText(t => t.At(245, 300).BoundTo("test5").Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=255"));

            page.AddLightShadowGauge(g => g.At(470, 200).BoundTo("test6", 0, 100)
                .Colors("r=0,g=255,b=0,a=255", "r=0,g=0,b=255,a=255", arcWidth: 6).Geometry(radius: 60));
            page.AddText(t => t.At(455, 300).BoundTo("test6").Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=255"));

            // Row 3 (y=380): Text / MultilineText / ShadowText - test7/8/9.
            page.AddText(t => t.At(20, 380).BoundTo("test7").Font("Microsoft YaHei,28,-1,5,50,0,0,0,0,0").Color("r=0,g=255,b=0,a=255"));

            page.AddMultilineText(t => t.At(220, 380, 180, 100).BoundTo("test8")
                .Font("Microsoft YaHei,20,-1,5,50,0,0,0,0,0").Color("r=0,g=255,b=255,a=255"));

            page.AddShadowText(t => t.At(20, 500).BoundTo("test9")
                .Font("Microsoft YaHei,50,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=0,a=255")
                .Border("r=23,g=54,b=255,a=255", 5).Shadow("r=0,g=0,b=0,a=128", 10));

            // Row 3, right side: DigitalClock (hour:minute:second). The digits are laid out
            // inside each item's own box and there is no letter-spacing field, so the box has
            // to be big enough or the two digits overlap - see Mk20Control.Protocol.API.md.
            page.AddText(t => t.At(470, 380).Text("Clock").Font("Microsoft YaHei,14,-1,5,50,0,0,0,0,0").Color("r=255,g=255,b=255,a=200"));
            page.AddDigitalClockField(c => c.At(444, 410, 64, 52, z: 2).Field("hour").Font("Microsoft YaHei,28,-1,5,75,0,0,0,0,0").Colors(
                "r=255,g=255,b=0,a=255", "r=0,g=0,b=0,a=0", "r=0,g=0,b=0,a=0"));
            page.AddDigitalClockField(c => c.At(508, 410, 64, 52, z: 2).Field("minute").Font("Microsoft YaHei,28,-1,5,75,0,0,0,0,0").Colors(
                "r=255,g=255,b=0,a=255", "r=0,g=0,b=0,a=0", "r=0,g=0,b=0,a=0"));
            page.AddDigitalClockField(c => c.At(572, 410, 64, 52, z: 2).Field("second").Font("Microsoft YaHei,28,-1,5,75,0,0,0,0,0").Colors(
                "r=255,g=255,b=0,a=255", "r=0,g=0,b=0,a=0", "r=0,g=0,b=0,a=0"));
        });

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BuildTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var items = decoded.Pages[0].Items;

        Assert.That(decoded.Pages, Has.Count.EqualTo(1));
        Assert.That(decoded.Pages[0].Encoder, Is.Not.Null);
        Assert.That(items.OfType<ProgressBarItem>().Any(), Is.True);
        Assert.That(items.OfType<LinearGaugeItem>().Any(), Is.True);
        Assert.That(items.OfType<RadialGaugeItem>().Any(), Is.True);
        Assert.That(items.OfType<CircularGaugeItem>().Any(), Is.True);
        Assert.That(items.OfType<SegmentedCircularGaugeItem>().Any(), Is.True);
        Assert.That(items.OfType<LightShadowGaugeItem>().Any(), Is.True);
        Assert.That(items.OfType<MultilineTextItem>().Any(), Is.True);
        Assert.That(items.OfType<ShadowTextItem>().Any(), Is.True);
        Assert.That(items.OfType<DigitalClockItem>().Count(), Is.EqualTo(3));
        Assert.That(items.OfType<TextItem>().Count(), Is.GreaterThanOrEqualTo(5));
        Assert.That(items.OfType<KeyItem>().Any(), Is.False, "This theme intentionally has no buttons/keys.");

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-widget-test-scratch-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}, {items.Count} item(s), 0 keys");
    }
}
