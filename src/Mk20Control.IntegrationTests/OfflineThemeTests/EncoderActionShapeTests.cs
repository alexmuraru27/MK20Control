using Mk20Control.Protocol.Theme.Building;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Pins the encoder actions to the exact field set and order real vendor themes carry.
///
/// VERIFIED AGAINST THE VENDOR APP ITSELF: a theme built by this library was opened in
/// ScreenKeyWindows, its encoders reassigned there (left = keyboard 1/2/3, right = system
/// volume), re-saved, and the result diffed against what these factories produce - every
/// field, every value and the field ORDER match exactly.
///
/// This exists because these factories previously emitted only <c>type</c> and
/// <c>category</c> - four of the six fields a real encoder action carries were missing - and
/// uploading a theme built with them twice left the device unresponsive (no <c>FILE_END</c>
/// ack, no <c>FIND_DEVICE</c> reply, physical replug required). With the full field set the
/// same upload was repeated twice on real hardware: both <c>FILE_END</c> and
/// <c>SET_DEVICE_RELOAD</c> acknowledged, and the device stayed responsive.
/// </summary>
public class EncoderActionShapeTests
{
    [Test]
    public void EncoderFunction_EmitsTheConfirmedVendorFieldOrder()
    {
        // Real: type, relatedTheme, parentDescription, iconPath, description, category.
        var action = KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume, "C:/themes/system_volume.Theme");

        Assert.That(action.RawFields.Keys, Is.EqualTo(new[]
        {
            "type", "relatedTheme", "parentDescription", "iconPath", "description", "category",
        }));
    }

    [Test]
    public void EncoderFunction_WithoutARelatedTheme_OmitsThatFieldOnly()
    {
        // encoder_system_media carries no relatedTheme in defaultTheme.Theme; the rest match.
        var action = KeyActions.EncoderFunction(EncoderFunctionType.SystemMedia);

        Assert.That(action.RawFields.Keys, Is.EqualTo(new[]
        {
            "type", "parentDescription", "iconPath", "description", "category",
        }));
    }

    [TestCase(EncoderFunctionType.SystemVolume, "encoder_system_volume", "/static/icon/white/systemVolume.png", "System volume")]
    [TestCase(EncoderFunctionType.DeviceBrightness, "encoder_device_brightness", "/static/icon/white/deviceBrightness.png", "Device brightness")]
    [TestCase(EncoderFunctionType.SystemMedia, "encoder_system_media", "/static/icon/white/systemMedia.png", "System audio")]
    public void EncoderFunction_CarriesTheConfirmedRealMetadata(
        EncoderFunctionType type, string expectedRawType, string expectedIconPath, string expectedDescription)
    {
        var action = KeyActions.EncoderFunction(type);

        Assert.Multiple(() =>
        {
            Assert.That(action.RawType, Is.EqualTo(expectedRawType));
            Assert.That(action.IconPath, Is.EqualTo(expectedIconPath));
            Assert.That(action.Description, Is.EqualTo(expectedDescription));
            Assert.That(action.ParentDescription, Is.EqualTo("Encoder"));
            Assert.That(action.Category, Is.EqualTo("encoder"));
        });
    }

    [Test]
    public void EncoderKeyboard_EmitsTheConfirmedVendorFieldOrder()
    {
        // Real (defaultTheme.Theme): the keycode pairs are written RIGHT, MIDDLE, LEFT -
        // not left-to-right - and description/category come last.
        var action = KeyActions.EncoderKeyboard(170, "Vol -", 168, "Mute", 169, "Vol +");

        Assert.That(action.RawFields.Keys, Is.EqualTo(new[]
        {
            "type", "parentDescription", "iconPath",
            "encoder_right_keycode", "encoder_right_keyString",
            "encoder_middle_keycode", "encoder_middle_keyString",
            "encoder_left_keycode", "encoder_left_keyString",
            "description", "category",
        }));
    }

    [Test]
    public void EncoderKeyboard_KeepsEachMotionsKeycodeWithItsOwnLabel()
    {
        // The vendor's own volume binding: rotate-left = Vol-, click = Mute, rotate-right = Vol+.
        var action = KeyActions.EncoderKeyboard(170, "Vol -", 168, "Mute", 169, "Vol +");

        Assert.Multiple(() =>
        {
            Assert.That(action.LeftKeycode, Is.EqualTo(170));
            Assert.That(action.MiddleKeycode, Is.EqualTo(168));
            Assert.That(action.RightKeycode, Is.EqualTo(169));
            Assert.That(action.RawFields["encoder_left_keyString"].AsString, Is.EqualTo("Vol -"));
            Assert.That(action.RawFields["encoder_middle_keyString"].AsString, Is.EqualTo("Mute"));
            Assert.That(action.RawFields["encoder_right_keyString"].AsString, Is.EqualTo("Vol +"));
        });
    }

    [Test]
    public void MatchesTheThemeScreenKeyWindowsItselfSaved()
    {
        // Ground truth: ScreenKeyWindows was given a theme built by this library, its left
        // encoder was set to keyboard 1/2/3 and its right to system volume, and it re-saved
        // the file. These are the exact values it wrote - digits carry a two-line label with
        // the shifted character above the base one, and click/left/right map to 1/2/3.
        var keyboard = KeyActions.EncoderKeyboard(31, "@\n2", 30, "!\n1", 32, "#\n3");

        Assert.Multiple(() =>
        {
            Assert.That(keyboard.RawFields["iconPath"].AsString, Is.EqualTo("/static/icon/white/keyboard.png"));
            Assert.That(keyboard.RawFields["parentDescription"].AsString, Is.EqualTo("Encoder"));
            Assert.That(keyboard.RawFields["description"].AsString, Is.EqualTo("Keyboard"));
            Assert.That(keyboard.RawFields["encoder_right_keycode"].AsInt32, Is.EqualTo(32));
            Assert.That(keyboard.RawFields["encoder_middle_keycode"].AsInt32, Is.EqualTo(30));
            Assert.That(keyboard.RawFields["encoder_left_keycode"].AsInt32, Is.EqualTo(31));
        });

        var volume = KeyActions.EncoderFunction(
            EncoderFunctionType.SystemVolume,
            EncoderPositions.RelatedThemePath(
                "C:/Users/Alex/Desktop/MK20/MK20Software/ScreenKeyWindows_v1_1", EncoderFunctionType.SystemVolume));

        Assert.That(volume.RawFields["relatedTheme"].AsString, Is.EqualTo(
            "C:/Users/Alex/Desktop/MK20/MK20Software/ScreenKeyWindows_v1_1/theme/MK20/Encoder/relatedTheme/system_volume.Theme"));
    }

    [Test]
    public void RelatedThemePath_NormalisesWindowsSeparatorsAndTrailingSlashes()
    {
        // Vendor paths use forward slashes even on Windows, so a caller passing a normal
        // Windows path must still produce a vendor-shaped value.
        string fromWindowsPath = EncoderPositions.RelatedThemePath(
            @"C:\Apps\ScreenKeyWindows_v1_1\", EncoderFunctionType.DeviceBrightness);

        Assert.That(fromWindowsPath, Is.EqualTo(
            "C:/Apps/ScreenKeyWindows_v1_1/theme/MK20/Encoder/relatedTheme/device_brightness.Theme"));
    }

    [Test]
    public void EncoderKeyboardCombo_PacksModifiersExactlyLikeTheVendorApp()
    {
        // Ground truth: ScreenKeyWindows was asked to bind the left encoder's CLICK to
        // Ctrl+Shift+C and re-saved the theme. It wrote keycode 774 (0x0306 - LeftCtrl|
        // LeftShift in the upper byte, HID 'C' = 0x06 in the lower) and the label
        // "L Ctrl L Shift C", leaving both rotate slots at keycode 0 with an empty label.
        var action = KeyActions.EncoderKeyboard(
            rotateLeft: null,
            click: (KeyModifiers.LeftCtrl | KeyModifiers.LeftShift, HidKey.C),
            rotateRight: null);

        Assert.Multiple(() =>
        {
            Assert.That(action.MiddleKeycode, Is.EqualTo(774));
            Assert.That(action.MiddleKeyLabel, Is.EqualTo("L Ctrl L Shift C"));

            // An unbound motion is keycode 0 / empty label - which the vendor writes too, so
            // it is NOT an invalid value.
            Assert.That(action.LeftKeycode, Is.EqualTo(0));
            Assert.That(action.LeftKeyLabel, Is.Empty);
            Assert.That(action.RightKeycode, Is.EqualTo(0));
            Assert.That(action.RightKeyLabel, Is.Empty);
        });
    }

    [Test]
    public void EncoderKeyboardCombo_BindsADifferentComboToEachMotion()
    {
        var action = KeyActions.EncoderKeyboard(
            rotateLeft: (KeyModifiers.LeftCtrl, HidKey.Z),
            click: (KeyModifiers.None, HidKey.Enter),
            rotateRight: (KeyModifiers.LeftCtrl, HidKey.Y));

        Assert.Multiple(() =>
        {
            Assert.That(action.LeftKeycode, Is.EqualTo((1 << 8) | (int)HidKey.Z));
            Assert.That(action.LeftKeyLabel, Is.EqualTo("L Ctrl Z"));
            // No modifiers means the upper byte stays clear - a plain keystroke.
            Assert.That(action.MiddleKeycode, Is.EqualTo((int)HidKey.Enter));
            Assert.That(action.MiddleKeyLabel, Is.EqualTo("Enter"));
            Assert.That(action.RightKeycode, Is.EqualTo((1 << 8) | (int)HidKey.Y));
        });
    }
}
