using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items.Widgets;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Pins the shape of the editor's two horizontal bars, which the vendor UI presents as
/// separate widgets but which are ordinary item types on the wire:
///
///   type 102 - rounded progress bar: carries "corner_radius" and the "lineargradient_*" pair
///   type 103 - the segmented/rectangular bar the editor calls "seg-hor": carries NEITHER
///
/// Confirmed 2026-08-14 by authoring a "seg-hor" bar in ScreenKeyWindows and saving it: the
/// resulting file decoded to a plain <see cref="LinearGaugeItem"/> (type 103), re-encoded
/// byte-identically through this library, and uploaded and rendered on real hardware. So
/// "seg-hor" needs no new item type - <c>AddLinearGauge</c> already produces it.
/// No hardware required.
/// </summary>
public class HorizontalBarVariantTests
{
    [Test]
    public void LinearGauge_IsType103_WithoutRoundedBarFields()
    {
        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            page.AddLinearGauge(gauge => gauge
                .At(276, 60, 100, 20)
                .BoundTo("CPU Usage", 0, 100)
                .Colors(new ThemeColor(0, 255, 0), new ThemeColor(255, 0, 0), new ThemeColor(0, 0, 255)));
        });

        var decoded = ThemeFileCodec.Decode(ThemeFileCodec.Encode(builder.Build()));
        var bar = decoded.Pages[0].Items.OfType<LinearGaugeItem>().Single();
        var fields = bar.RawJson.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(bar.RawTypeCode, Is.EqualTo("103"));

            // These two are exactly what separates the rounded bar (102) from this one.
            Assert.That(fields, Does.Not.Contain("corner_radius"),
                "a seg-hor bar carries no corner radius - that field belongs to type 102");
            Assert.That(fields, Does.Not.Contain("lineargradient_flag"),
                "a seg-hor bar carries no linear-gradient fields - those belong to type 102");

            // ...and the fields a real vendor-authored seg-hor bar does carry.
            Assert.That(fields, Does.Contain("front_color"));
            Assert.That(fields, Does.Contain("back_color"));
            Assert.That(fields, Does.Contain("border_color"));
            Assert.That(fields, Does.Contain("border_width"));
            Assert.That(fields, Does.Contain("system_data_name"));
            Assert.That(fields, Does.Contain("system_data_min_value"));
            Assert.That(fields, Does.Contain("system_data_max_value"));
        });
    }

    [Test]
    public void ProgressBar_IsType102_WithRoundedBarFields()
    {
        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            page.AddProgressBar(bar => bar
                .At(276, 60, 100, 20)
                .BoundTo("CPU Usage", 0, 100)
                .Colors(new ThemeColor(0, 255, 0), new ThemeColor(255, 0, 0), new ThemeColor(0, 0, 255)));
        });

        var decoded = ThemeFileCodec.Decode(ThemeFileCodec.Encode(builder.Build()));
        var bar = decoded.Pages[0].Items.OfType<ProgressBarItem>().Single();
        var fields = bar.RawJson.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(bar.RawTypeCode, Is.EqualTo("102"));
            Assert.That(fields, Does.Contain("corner_radius"));
            Assert.That(fields, Does.Contain("lineargradient_flag"));
        });
    }
}
