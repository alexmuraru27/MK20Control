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
/// Builds a theme that assigns the MK20's two physical rotary encoders to built-in device
/// functions: the left encoder to system volume, the right encoder to screen (device)
/// backlight brightness - each fully functional but rendered **invisible** on the secondary
/// screen (icon opacity 0, progress-bar/text colors fully transparent, alpha=0) since the
/// encoder function itself does not depend on anything being visibly drawn - confirmed
/// real-hardware layout (cross-checked against <c>defaultTheme.Theme</c> and
/// <c>海边吹风.Theme</c>): an encoder function is a normal <see cref="KeyItem"/> (type 115)
/// positioned at a fixed secondary-screen coordinate - the LEFT encoder is always at
/// <c>x=106, y=0</c>, the RIGHT encoder always at <c>x=320, y=0</c> (both distinct from the
/// row/col matrix grid used by physical keys) - with its <c>Action</c> set to an
/// <see cref="EncoderFunctionAction"/> built via <c>KeyActions.EncoderFunction(rawType)</c>.
/// Confirmed real <c>rawType</c> values: <c>"encoder_system_volume"</c>,
/// <c>"encoder_device_brightness"</c>, <c>"encoder_system_media"</c>. No hardware required
/// for this test; see <c>HardwareTests.EncoderVolumeAndBrightnessUploadTests</c> for the
/// live-hardware variant.
/// </summary>
public class EncoderVolumeAndBrightnessThemeTests
{
    /// <summary>Confirmed real fixed position for the left physical rotary encoder.</summary>
    public const double LeftEncoderX = 106, LeftEncoderY = 0;

    /// <summary>Confirmed real fixed position for the right physical rotary encoder.</summary>
    public const double RightEncoderX = 320, RightEncoderY = 0;

    /// <summary>Fully transparent color (alpha=0) used to hide the progress-bar/text readouts while keeping the encoder binding functional.</summary>
    private const string Transparent = "r=0,g=0,b=0,a=0";

    public static byte[] BuildTheme()
    {
        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);

            // Left encoder -> system volume, confirmed real icon path
            // "/static/icon/white/systemVolume_.png" (observed in defaultTheme.Theme).
            // Opacity 0 hides the icon; the encoder function itself is unaffected.
            page.AddKey(0, 0, key => key
                .At(LeftEncoderX, LeftEncoderY)
                .IconAssetPath("/static/icon/white/systemVolume_.png")
                .Opacity(0)
                .Action(KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)));
            page.AddProgressBar(pb => pb.At(204, 96, 100, 12).BoundTo("Volume", 0, 100)
                .Colors(Transparent, Transparent, Transparent));
            page.AddText(t => t.At(229, 62).BoundTo("Volume").Color(Transparent));

            // Right encoder -> device (screen) brightness, confirmed real icon path
            // "/static/icon/white/deviceBrightness_.png" (observed in 海边吹风.Theme).
            page.AddKey(0, 0, key => key
                .At(RightEncoderX, RightEncoderY)
                .IconAssetPath("/static/icon/white/deviceBrightness_.png")
                .Opacity(0)
                .Action(KeyActions.EncoderFunction(EncoderFunctionType.DeviceBrightness)));
            page.AddProgressBar(pb => pb.At(420, 96, 100, 12).BoundTo("device_bl", 0, 100)
                .Colors(Transparent, Transparent, Transparent));
            page.AddText(t => t.At(444, 62).BoundTo("device_bl").Color(Transparent));

            // A single plain content key so the page has at least one ordinary physical key.
            page.AddKey(0, 0, key => key
                .Icon("icon_01.png", File.ReadAllBytes(TestPaths.IconFile(1)))
                .Action(KeyActions.Keyboard(HidKey.Digit1, "1")));
        });

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BuildTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildTheme();

        var decoded = ThemeFileCodec.Decode(encoded);
        var keys = decoded.Pages[0].Items.OfType<KeyItem>().ToList();

        var leftEncoderKey = keys.FirstOrDefault(k => k.X == LeftEncoderX && k.Y == LeftEncoderY);
        var rightEncoderKey = keys.FirstOrDefault(k => k.X == RightEncoderX && k.Y == RightEncoderY);

        Assert.That(decoded.Pages, Has.Count.EqualTo(1));
        Assert.That(decoded.Pages[0].Encoder, Is.Not.Null);

        Assert.That(leftEncoderKey, Is.Not.Null, "Left encoder key item missing.");
        Assert.That(leftEncoderKey!.Action, Is.InstanceOf<EncoderFunctionAction>());
        Assert.That(((EncoderFunctionAction)leftEncoderKey.Action!).RawType, Is.EqualTo("encoder_system_volume"));
        Assert.That(leftEncoderKey.IconAssetPath, Is.EqualTo("/static/icon/white/systemVolume_.png"));
        Assert.That(leftEncoderKey.RawJson.GetProperty("opacity").GetString(), Is.EqualTo("0"), "Left encoder icon should be invisible (opacity 0).");

        Assert.That(rightEncoderKey, Is.Not.Null, "Right encoder key item missing.");
        Assert.That(rightEncoderKey!.Action, Is.InstanceOf<EncoderFunctionAction>());
        Assert.That(((EncoderFunctionAction)rightEncoderKey.Action!).RawType, Is.EqualTo("encoder_device_brightness"));
        Assert.That(rightEncoderKey.IconAssetPath, Is.EqualTo("/static/icon/white/deviceBrightness_.png"));
        Assert.That(rightEncoderKey.RawJson.GetProperty("opacity").GetString(), Is.EqualTo("0"), "Right encoder icon should be invisible (opacity 0).");

        var progressBars = decoded.Pages[0].Items.OfType<ProgressBarItem>().ToList();
        var volumeBar = progressBars.FirstOrDefault(p => p.SystemDataName == "Volume");
        var brightnessBar = progressBars.FirstOrDefault(p => p.SystemDataName == "device_bl");
        Assert.That(volumeBar, Is.Not.Null, "Volume progress bar missing.");
        Assert.That(brightnessBar, Is.Not.Null, "device_bl progress bar missing.");
        Assert.That(volumeBar!.RawJson.GetProperty("front_color").GetString(), Is.EqualTo("r=0,g=0,b=0,a=0"), "Volume readout should be fully transparent.");
        Assert.That(brightnessBar!.RawJson.GetProperty("front_color").GetString(), Is.EqualTo("r=0,g=0,b=0,a=0"), "Brightness readout should be fully transparent.");

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-encoder-volume-brightness-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}, {decoded.Assets.Count} asset(s)");
    }
}
