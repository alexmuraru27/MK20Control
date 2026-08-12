namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// An encoder (rotary knob) function assignment - "type" observed as "encoder_system_volume",
/// "encoder_system_media", "encoder_device_brightness", and "encoder_keyboard" across
/// several real retail themes and <c>defaultTheme.Theme</c>. All variants share
/// <c>category: "encoder"</c> (preserved via <see cref="KeyAction.RawFields"/>); the exact
/// operational fields vary by <see cref="KeyAction.RawType"/>:
///
///   - "encoder_system_volume" / "encoder_device_brightness": bind to a separate
///     <see cref="RelatedThemePath"/> (a host-side <c>.Theme</c> file path) that is loaded
///     onto the small per-encoder display while this function is active - confirmed via
///     DEVICE_ProactiveEscalationCMD capture and matching <c>Encoder\relatedTheme\*.Theme</c>
///     files on disk.
///   - "encoder_system_media": no extra fields beyond the common ones were observed -
///     appears to be a fixed/built-in behavior with no further configuration.
///   - "encoder_keyboard": see <see cref="EncoderKeyboardAction"/> for its extra
///     rotate-left/click/rotate-right keycode assignments.
/// </summary>
public record EncoderFunctionAction : KeyAction
{
    /// <summary>Always observed as "encoder" for this action family.</summary>
    public string? Category { get; init; }

    /// <summary>
    /// The host-side path to a <c>.Theme</c> file shown on the small encoder display while
    /// this function is active (only present for "encoder_system_volume" and
    /// "encoder_device_brightness" so far); null for variants without this field.
    /// </summary>
    public string? RelatedThemePath { get; init; }
}

/// <summary>
/// An "encoder_keyboard" assignment: a keyboard keystroke is bound to each of the encoder's
/// three physical actions (rotate left, press/click, rotate right). Confirmed present in
/// <c>defaultTheme.Theme</c> bound to system volume down/mute/up
/// (keycodes 170/168/169, HID consumer-control usages for Volume-/Mute/Volume+).
/// </summary>
public sealed record EncoderKeyboardAction : EncoderFunctionAction
{
    public int LeftKeycode { get; init; }
    public string? LeftKeyLabel { get; init; }
    public int MiddleKeycode { get; init; }
    public string? MiddleKeyLabel { get; init; }
    public int RightKeycode { get; init; }
    public string? RightKeyLabel { get; init; }
}
