namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Built-in encoder (rotary knob) function types for <see cref="KeyActions.EncoderFunction"/>
/// - confirmed real <c>type</c> string values observed across multiple real vendor themes
/// (<c>defaultTheme.Theme</c>, <c>海边吹风.Theme</c>) and <c>Encoder\relatedTheme\*.Theme</c>
/// files on disk. Use this enum instead of a raw string to avoid typos in the encoded
/// action's <c>type</c> field.
/// </summary>
public enum EncoderFunctionType
{
    /// <summary>Encoder adjusts the OS/system audio volume. Confirmed real string: "encoder_system_volume".</summary>
    SystemVolume,

    /// <summary>Encoder adjusts the device's screen backlight brightness. Confirmed real string: "encoder_device_brightness".</summary>
    DeviceBrightness,

    /// <summary>Encoder controls system media playback (play/pause/skip via click+rotate). Confirmed real string: "encoder_system_media".</summary>
    SystemMedia,
}

/// <summary>Maps <see cref="EncoderFunctionType"/> values to their confirmed real wire-format <c>type</c> strings, and back.</summary>
public static class EncoderFunctionTypeExtensions
{
    /// <summary>Returns the confirmed real wire-format <c>type</c> string for this <see cref="EncoderFunctionType"/> (e.g. "encoder_system_volume").</summary>
    public static string ToRawType(this EncoderFunctionType type) => type switch
    {
        EncoderFunctionType.SystemVolume => "encoder_system_volume",
        EncoderFunctionType.DeviceBrightness => "encoder_device_brightness",
        EncoderFunctionType.SystemMedia => "encoder_system_media",
        _ => throw new System.ArgumentOutOfRangeException(nameof(type), type, "Unknown EncoderFunctionType."),
    };
}
