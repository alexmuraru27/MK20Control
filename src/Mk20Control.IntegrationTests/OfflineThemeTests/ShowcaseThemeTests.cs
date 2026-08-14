using Mk20Control.IntegrationTests.Support;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// The end-to-end showcase theme: everything this library is meant to do, on one page.
///
///   - an animated GIF background on the MAIN screen
///   - an animated GIF background on the SECONDARY screen
///   - buttons whose icons are PNGs WITH ALPHA (a ring with a punched-out centre, a 50%
///     wash, a 0->255 alpha sweep and a checkerboard) plus a text title over each, so you
///     can see on the physical device whether transparent pixels really let the animated
///     background show through the button artwork
///   - both rotary ENCODERS bound to command ids
///   - every button bound to a command id, so a host application can attach its own C# via
///     <see cref="Mk20Control.Protocol.Host.KeyBindings"/>
///
/// Item order matters: backgrounds are added first so the keys composite on top of them.
/// </summary>
public class ShowcaseThemeTests
{
    /// <summary>Command ids for the alpha-test buttons, in grid order along the top row.</summary>
    public static readonly (string Id, string IconFile, string Title)[] AlphaButtons =
    {
        ("alpha.ring",     "alpha_ring.png",     "RING"),
        ("alpha.half",     "alpha_half.png",     "50%"),
        ("alpha.gradient", "alpha_gradient.png", "FADE"),
        ("alpha.checker",  "alpha_checker.png",  "CHECK"),
    };

    /// <summary>An opaque icon in the same row, as the control: if the background shows through the alpha icons but not this one, alpha is genuinely being honoured.</summary>
    public const string OpaqueControlId = "alpha.opaque";

    public const string LeftEncoderId = "enc.left";
    public const string RightEncoderId = "enc.right";

    private static string AlphaIcon(string fileName)
    {
        string path = Path.Combine(TestPaths.IconsDir, fileName);
        Assert.That(File.Exists(path), Is.True,
            $"Missing {fileName}. Run: dotnet run --project tools\\AssetGenerator");
        return path;
    }

    public static byte[] BuildShowcaseTheme()
    {
        byte[] mainGif = File.ReadAllBytes(TestPaths.GifFile("mooglevibin.gif"));
        byte[] secondaryGif = File.ReadAllBytes(TestPaths.GifFile("pop-cat.gif"));
        byte[] opaqueIcon = File.ReadAllBytes(TestPaths.IconFile(7));

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);

            // Backgrounds first so every key draws over them - this is what makes the alpha
            // test meaningful: a transparent pixel should reveal the animation beneath.
            page.AddDynamicImage(img => img.MainScreenBackgroundAutoFit("bg_main.gif", mainGif));
            page.AddDynamicImage(img => img.SecondaryScreenBackgroundAutoFit("bg_secondary.gif", secondaryGif));

            // Top row: the four alpha icons, each labelled. IconPreservingAlpha keeps the
            // alpha channel; the normal .Icon() path would flatten it onto black, which would
            // make this test meaningless.
            for (int col = 0; col < AlphaButtons.Length; col++)
            {
                var (id, iconFile, title) = AlphaButtons[col];
                byte[] bytes = File.ReadAllBytes(AlphaIcon(iconFile));
                page.AddKey(0, col, key => key
                    .IconPreservingAlpha(iconFile, bytes)
                    .Title(title)
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.Command(id)));
            }

            // Same row, last column: a fully opaque icon as the comparison control.
            page.AddKey(0, 4, key => key
                .Icon("icon_07.png", opaqueIcon)
                .Title("OPAQUE")
                .TitleStyle(fontSize: 20, color: ThemeColor.White)
                .Action(KeyActions.Command(OpaqueControlId)));

            // Second row: plain command buttons, to exercise the listener path. These use the
            // ORDINARY .Icon() path (alpha flattened onto black) so the two rows sit side by
            // side on the device as a direct visual comparison.
            for (int col = 0; col < 5; col++)
            {
                string id = $"btn.{col}";
                page.AddKey(1, col, key => key
                    .Icon("alpha_ring.png", File.ReadAllBytes(AlphaIcon("alpha_ring.png")))
                    .Title($"BTN {col}")
                    .TitleStyle(fontSize: 20, color: ThemeColor.White)
                    .Action(KeyActions.Command(id)));
            }

            // Both encoders routed to our own C# rather than a built-in function. They are
            // invisible by convention (opacity 0) - the binding does not depend on artwork.
            page.AddEncoder(EncoderSide.Left, key => key
                .IconAssetPath(EncoderPositions.SystemVolumeIcon)
                .Opacity(0)
                .Action(KeyActions.Command(LeftEncoderId)));

            page.AddEncoder(EncoderSide.Right, key => key
                .IconAssetPath(EncoderPositions.DeviceBrightnessIcon)
                .Opacity(0)
                .Action(KeyActions.Command(RightEncoderId)));
        });

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void Showcase_ContainsBothGifBackgrounds()
    {
        var decoded = ThemeFileCodec.Decode(BuildShowcaseTheme());

        var gifAssets = decoded.Assets.Where(a => a.Path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.That(gifAssets, Has.Count.GreaterThanOrEqualTo(2), "expected a main-screen and a secondary-screen GIF");

        // Both backgrounds are type-114 dynamic images; the two screens use different asset
        // namespaces, which is what tells them apart on the device.
        var images = decoded.Pages[0].Items.OfType<DynamicImageItem>().ToList();
        Assert.That(images, Has.Count.EqualTo(2));
        Assert.That(images.Select(i => i.AssetPath).Distinct().Count(), Is.EqualTo(2),
            "main and secondary backgrounds must not collide on the same asset path");
    }

    [Test]
    public void Showcase_AlphaIconsAreActuallyTransparent()
    {
        // The point of the theme: if these were opaque the on-device test would prove nothing.
        var decoded = ThemeFileCodec.Decode(BuildShowcaseTheme());

        foreach (var (_, iconFile, _) in AlphaButtons)
        {
            var asset = decoded.Assets.FirstOrDefault(a => a.Path.EndsWith(iconFile, StringComparison.OrdinalIgnoreCase));
            Assert.That(asset, Is.Not.Null, $"{iconFile} was not embedded");
            Assert.That(PngHasTransparency(asset!.Data), Is.True,
                $"{iconFile} carries no transparent or partially transparent pixels, so it cannot demonstrate see-through");
        }

        // ...and the control icon must NOT be transparent, or the comparison is meaningless.
        var opaque = decoded.Assets.First(a => a.Path.EndsWith("icon_07.png", StringComparison.OrdinalIgnoreCase));
        Assert.That(PngHasTransparency(opaque.Data), Is.False, "the control icon is supposed to be fully opaque");
    }

    [Test]
    public void TheOrdinaryIconPath_FlattensAlphaOntoBlack()
    {
        // Documents why IconPreservingAlpha exists: the normal path deliberately matches the
        // vendor format (128x128, RGB, no alpha channel), so a transparent source becomes
        // black. Row 1 of the showcase uses it on the very same ring PNG as row 0.
        byte[] ringSource = File.ReadAllBytes(AlphaIcon("alpha_ring.png"));
        Assert.That(PngHasTransparency(ringSource), Is.True, "the source ring must be transparent to begin with");

        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            page.AddKey(0, 0, key => key.Icon("flattened.png", ringSource));
            page.AddKey(0, 1, key => key.IconPreservingAlpha("kept.png", ringSource));
        });

        var decoded = ThemeFileCodec.Decode(ThemeFileCodec.Encode(builder.Build()));
        var flattened = decoded.Assets.First(a => a.Path.Contains("flattened", StringComparison.OrdinalIgnoreCase));
        var kept = decoded.Assets.First(a => a.Path.Contains("kept", StringComparison.OrdinalIgnoreCase));

        Assert.Multiple(() =>
        {
            Assert.That(PngHasTransparency(flattened.Data), Is.False, ".Icon() must match the vendor RGB format");
            Assert.That(PngHasTransparency(kept.Data), Is.True, ".IconPreservingAlpha() must keep the alpha channel");
            // Both must still be the confirmed real 128x128 icon size.
            Assert.That(PngSize(flattened.Data), Is.EqualTo((128, 128)));
            Assert.That(PngSize(kept.Data), Is.EqualTo((128, 128)));
        });
    }

    private static (int Width, int Height) PngSize(byte[] png) =>
        (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
         System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));

    [Test]
    public void Showcase_EveryButtonAndEncoderCarriesItsCommandId()
    {
        var decoded = ThemeFileCodec.Decode(BuildShowcaseTheme());
        var ids = decoded.Pages[0].Items.OfType<KeyItem>()
            .Select(k => k.Action).OfType<TextInputAction>()
            .Select(a => a.InputText).ToList();

        var expected = AlphaButtons.Select(b => b.Id)
            .Append(OpaqueControlId)
            .Concat(Enumerable.Range(0, 5).Select(i => $"btn.{i}"))
            .Append(LeftEncoderId)
            .Append(RightEncoderId);

        Assert.That(ids, Is.EquivalentTo(expected));
    }

    [Test]
    public void Showcase_EncodersSitAtTheirConfirmedHardwarePositions()
    {
        var decoded = ThemeFileCodec.Decode(BuildShowcaseTheme());
        var encoders = decoded.Pages[0].Items.OfType<KeyItem>()
            .Where(k => (k.Action as TextInputAction)?.InputText is LeftEncoderId or RightEncoderId)
            .ToList();

        Assert.That(encoders, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            var left = encoders.Single(k => ((TextInputAction)k.Action!).InputText == LeftEncoderId);
            var right = encoders.Single(k => ((TextInputAction)k.Action!).InputText == RightEncoderId);
            Assert.That(left.X, Is.EqualTo(EncoderPositions.LeftX));
            Assert.That(left.Y, Is.EqualTo(EncoderPositions.LeftY));
            Assert.That(right.X, Is.EqualTo(EncoderPositions.RightX));
            Assert.That(right.Y, Is.EqualTo(EncoderPositions.RightY));
        });
    }

    /// <summary>True if the PNG has any pixel that is not fully opaque.</summary>
    private static bool PngHasTransparency(byte[] png)
    {
        // Colour type 6 = RGBA, 4 = grey+alpha; a tRNS chunk adds alpha to other types.
        byte colourType = png[25];
        if (colourType is not (4 or 6))
            return ContainsChunk(png, "tRNS");

        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(png);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image[x, y].A != 255) return true;

        return false;
    }

    private static bool ContainsChunk(byte[] png, string chunkType)
    {
        byte[] needle = System.Text.Encoding.ASCII.GetBytes(chunkType);
        for (int i = 8; i + needle.Length <= png.Length; i++)
        {
            if (png.AsSpan(i, needle.Length).SequenceEqual(needle)) return true;
        }
        return false;
    }
}
