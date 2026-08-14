using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items.Widgets;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Covers <see cref="ThemeColor"/>, the typed replacement for hand-written
/// <c>"r=0,g=170,b=255,a=220"</c> colour strings.
///
/// The critical property is that adopting it changes no emitted bytes: real theme files
/// contain zero-padded components (<c>"r=000,g=000,b=255,a=255"</c>), so a colour parsed from
/// text must reproduce that text exactly rather than a normalised form.
/// </summary>
public class ThemeColorTests
{
    [Test]
    public void Components_RenderAsTheWidgetWireForm()
    {
        var colour = new ThemeColor(0, 170, 255, 220);

        Assert.Multiple(() =>
        {
            Assert.That(colour.ToWireString(), Is.EqualTo("r=0,g=170,b=255,a=220"));
            Assert.That(colour.ToString(), Is.EqualTo("r=0,g=170,b=255,a=220"));
            Assert.That(colour.R, Is.EqualTo(0));
            Assert.That(colour.G, Is.EqualTo(170));
            Assert.That(colour.B, Is.EqualTo(255));
            Assert.That(colour.A, Is.EqualTo(220));
        });
    }

    [Test]
    public void AlphaDefaultsToFullyOpaque()
    {
        Assert.That(new ThemeColor(1, 2, 3).A, Is.EqualTo(255));
    }

    [TestCase(-1)]
    [TestCase(256)]
    public void OutOfRangeComponents_AreRejected(int bad)
    {
        // The whole point of the type: catch a bad value at the call site instead of writing
        // it into a theme file.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThemeColor(bad, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThemeColor(0, bad, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThemeColor(0, 0, bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThemeColor(0, 0, 0, bad));
    }

    [Test]
    public void ParsedText_IsReproducedExactly()
    {
        // Real theme files zero-pad; re-encoding must not silently rewrite them.
        const string padded = "r=000,g=000,b=255,a=255";
        var colour = ThemeColor.Parse(padded);

        Assert.Multiple(() =>
        {
            Assert.That(colour.ToWireString(), Is.EqualTo(padded));
            Assert.That(colour.R, Is.EqualTo(0));
            Assert.That(colour.B, Is.EqualTo(255));
            // ...but it still equals the same colour written without padding.
            Assert.That(colour, Is.EqualTo(new ThemeColor(0, 0, 255, 255)));
        });
    }

    [Test]
    public void HexForm_IsAcceptedAndEmitted()
    {
        var colour = ThemeColor.Parse("#22D3EE");

        Assert.Multiple(() =>
        {
            Assert.That(colour, Is.EqualTo(new ThemeColor(0x22, 0xD3, 0xEE)));
            Assert.That(new ThemeColor(0x22, 0xD3, 0xEE).ToHexString(), Is.EqualTo("#22d3ee"));
            // A translucent colour needs the 8-digit form to survive.
            Assert.That(new ThemeColor(0x22, 0xD3, 0xEE, 0x80).ToHexString(), Is.EqualTo("#22d3ee80"));
            Assert.That(ThemeColor.Parse("#22d3ee80").A, Is.EqualTo(0x80));
        });
    }

    [Test]
    public void AStringLiteral_StillWorksWhereAColourIsExpected()
    {
        // The implicit conversion keeps raw wire strings usable, e.g. when copying a value
        // out of an existing theme.
        ThemeColor fromWire = "r=1,g=2,b=3,a=4";
        ThemeColor fromHex = "#010203";

        Assert.Multiple(() =>
        {
            Assert.That(fromWire, Is.EqualTo(new ThemeColor(1, 2, 3, 4)));
            Assert.That(fromHex, Is.EqualTo(new ThemeColor(1, 2, 3)));
        });
    }

    [TestCase("")]
    [TestCase("nonsense")]
    [TestCase("r=0,g=0")]
    [TestCase("r=0,g=0,b=300,a=0")]
    [TestCase("r=0,g=0,b=0,x=0")]
    [TestCase("#12345")]
    public void MalformedText_IsRejected(string text)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ThemeColor.TryParse(text, out _), Is.False);
            Assert.Throws<FormatException>(() => ThemeColor.Parse(text));
        });
    }

    [Test]
    public void Presets_CoverTheCommonCases()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ThemeColor.Transparent.A, Is.EqualTo(0));
            Assert.That(ThemeColor.Black, Is.EqualTo(new ThemeColor(0, 0, 0)));
            Assert.That(ThemeColor.White, Is.EqualTo(new ThemeColor(255, 255, 255)));
            Assert.That(ThemeColor.White.WithAlpha(128), Is.EqualTo(new ThemeColor(255, 255, 255, 128)));
        });
    }

    [Test]
    public void EqualityIgnoresSpelling()
    {
        var padded = ThemeColor.Parse("r=000,g=000,b=000,a=255");
        var hex = ThemeColor.Parse("#000000");

        Assert.Multiple(() =>
        {
            Assert.That(padded, Is.EqualTo(hex));
            Assert.That(padded == hex, Is.True);
            Assert.That(padded.GetHashCode(), Is.EqualTo(hex.GetHashCode()));
            // ...even though each still renders as it was written.
            Assert.That(padded.ToWireString(), Is.Not.EqualTo(hex.ToWireString()));
        });
    }

    [Test]
    public void AColourSetOnAWidget_ReachesTheEncodedTheme()
    {
        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            page.AddProgressBar(pb => pb.At(0, 0, 100, 12).BoundTo("CPU Usage")
                .Colors(new ThemeColor(0, 170, 255, 220), ThemeColor.Transparent, ThemeColor.Black));
        });

        var decoded = ThemeFileCodec.Decode(ThemeFileCodec.Encode(builder.Build()));
        var bar = decoded.Pages[0].Items.OfType<ProgressBarItem>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(bar.RawJson.GetProperty("front_color").GetString(), Is.EqualTo("r=0,g=170,b=255,a=220"));
            Assert.That(bar.RawJson.GetProperty("back_color").GetString(), Is.EqualTo("r=0,g=0,b=0,a=0"));
            Assert.That(bar.RawJson.GetProperty("border_color").GetString(), Is.EqualTo("r=0,g=0,b=0,a=255"));
        });
    }

    [Test]
    public void BuiltInDefaults_KeepTheirExactVendorSpelling()
    {
        // The DigitalClock border default is zero-padded because it was taken verbatim from a
        // real theme.
        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            page.AddDigitalClockField(c => c.Field("minute"));
        });

        var decoded = ThemeFileCodec.Decode(ThemeFileCodec.Encode(builder.Build()));
        var clock = decoded.Pages[0].Items.OfType<DigitalClockItem>().Single();

        Assert.That(clock.RawJson.GetProperty("border_color").GetString(), Is.EqualTo("r=000,g=000,b=255,a=255"));
    }
}
