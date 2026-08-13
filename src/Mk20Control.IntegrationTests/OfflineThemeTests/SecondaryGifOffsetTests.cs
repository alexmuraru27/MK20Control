using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Verifies <c>DynamicImageItemBuilder.SecondaryScreenBackgroundAutoFit</c>'s pan/offset
/// parameters actually change which part of the source GIF is kept (not silently ignored),
/// by building the same GIF at several different offsets and confirming the resulting
/// theme files differ. No hardware required. Formerly <c>Mk20Control.App</c>'s
/// <c>--build-secondary-gif-offset-test</c> CLI flag.
/// </summary>
public class SecondaryGifOffsetTests
{
    private static byte[] BuildWithOffset(double offsetX, double offsetY)
    {
        string gifPath = TestPaths.GifFile("pop-cat.gif");
        Assert.That(File.Exists(gifPath), Is.True, $"Missing GIF: {gifPath}");

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            page.AddDynamicImage(img => img.SecondaryScreenBackgroundAutoFit(
                "popcat_secondary_offset.gif", File.ReadAllBytes(gifPath), offsetX, offsetY));
        });
        return ThemeFileCodec.Encode(builder.Build());
    }

    [TestCase(-1, 0)]
    [TestCase(0, 0)]
    [TestCase(1, 0.5)]
    [TestCase(-0.7, -0.5)]
    public void BuildWithOffset_RoundTripsCorrectly(double offsetX, double offsetY)
    {
        byte[] encoded = BuildWithOffset(offsetX, offsetY);

        var decoded = ThemeFileCodec.Decode(encoded);
        var secondaryBg = decoded.Pages[0].Items.OfType<DynamicImageItem>().FirstOrDefault(d => d.BackgroundType == "secondary");

        Assert.That(secondaryBg, Is.Not.Null);
        Assert.That(secondaryBg!.X, Is.EqualTo(106));
        Assert.That(secondaryBg.Y, Is.EqualTo(0));
        Assert.That(secondaryBg.Width, Is.EqualTo(428));
        Assert.That(secondaryBg.Height, Is.EqualTo(142));

        string outPath = Path.Combine(Path.GetTempPath(), $"mk20-secondary-gif-offset-{offsetX}_{offsetY}-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}");
    }

    [Test]
    public void DifferentOffsets_ProduceDifferentAssetBytes()
    {
        byte[] centered = BuildWithOffset(0, 0);
        byte[] shiftedLeft = BuildWithOffset(-1, 0);
        byte[] shiftedDown = BuildWithOffset(0, 1);

        Assert.That(centered, Is.Not.EqualTo(shiftedLeft), "Offsetting horizontally should change the encoded asset bytes.");
        Assert.That(centered, Is.Not.EqualTo(shiftedDown), "Offsetting vertically should change the encoded asset bytes.");
    }
}
