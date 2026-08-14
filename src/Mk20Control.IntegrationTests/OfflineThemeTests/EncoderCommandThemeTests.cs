using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Experiment: can a rotary encoder be bound to an arbitrary command id (and therefore to
/// your own C#), instead of only to the vendor's three built-in functions?
///
/// What the captures already establish:
///   - An encoder assignment is an ordinary KeyItem placed at a fixed secondary-screen
///     coordinate (LEFT x=106, RIGHT x=320); its action lives in the key's controlData,
///     which is a free-form tagged-value map.
///   - Encoder activity is reported through the SAME DEVICE_ProactiveEscalationCMD channel
///     as a button press - pseudo-rows 100-105 (col == row, pressed=1, no release) - and
///     map[1] echoes the encoder key's full action descriptor, exactly like a button.
///
/// So the open question is only whether the device gates that echo on the action's type
/// starting with "encoder_", or simply echoes whatever the encoder key carries. This theme
/// tests both at once: the LEFT encoder gets a plain command id, the RIGHT encoder gets a
/// command id dressed with the encoder family's own <c>category="encoder"</c> marker.
/// </summary>
public class EncoderCommandThemeTests
{
    public const double LeftEncoderX = 106, LeftEncoderY = 0;
    public const double RightEncoderX = 320, RightEncoderY = 0;

    public const string LeftCommandId = "enc.left";
    public const string RightCommandId = "enc.right";

    /// <summary>
    /// A command action carrying the encoder family's <c>category="encoder"</c> field, in
    /// case the device uses that marker to decide a key is an encoder.
    /// </summary>
    public static TextInputAction EncoderCommand(string commandId)
    {
        var baseAction = KeyActions.Command(commandId);
        var fields = new Dictionary<string, TaggedValue>(baseAction.RawFields)
        {
            ["category"] = TaggedValue.Of("encoder"),
        };
        return baseAction with { RawFields = fields };
    }

    public static byte[] BuildTheme()
    {
        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);

            page.AddKey(0, 0, key => key
                .At(LeftEncoderX, LeftEncoderY)
                .IconDevice(DeviceIcon.EncoderSystemVolume)
                .Opacity(0)
                .Action(KeyActions.Command(LeftCommandId)));

            page.AddKey(0, 0, key => key
                .At(RightEncoderX, RightEncoderY)
                .IconDevice(DeviceIcon.EncoderDeviceBrightness)
                .Opacity(0)
                .Action(EncoderCommand(RightCommandId)));

            // The control key sits at a DIFFERENT grid cell (r1c1) so a physical button press
            // can never be confused with the encoder keys, which occupy r0c0 by convention.
            page.AddKey(1, 1, key => key
                .Icon("icon_01.png", File.ReadAllBytes(Support.TestPaths.IconFile(1)))
                .Title("CONTROL")
                .Action(KeyActions.Command("control.key")));
        });

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BothEncoderCommands_SurviveTheRoundTrip()
    {
        var decoded = ThemeFileCodec.Decode(BuildTheme());
        var actions = decoded.Pages[0].Items
            .OfType<Mk20Control.Protocol.Theme.Items.KeyItem>()
            .Select(k => k.Action)
            .OfType<TextInputAction>()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(actions.Select(a => a.InputText),
                Is.EqualTo(new[] { LeftCommandId, RightCommandId, "control.key" }));
            Assert.That(actions[1].RawFields.ContainsKey("category"), Is.True,
                "the right encoder's command must keep its category=encoder marker");
        });
    }
}
