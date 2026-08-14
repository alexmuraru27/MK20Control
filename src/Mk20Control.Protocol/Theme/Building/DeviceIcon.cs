namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// The device's own built-in key icons, selected by name instead of by path.
///
/// These are NOT theme assets: the artwork already exists on the device (and in the
/// ScreenKeyWindows install) under <c>/static/icon/...</c>, so a key can show one without
/// embedding any image bytes - which is exactly what real vendor themes do for their
/// navigation and encoder keys. Pass one to
/// <see cref="KeyItemBuilder.IconDevice"/> and the library resolves the path,
/// so a mistyped or renamed path cannot reach the device.
///
/// Every path behind these values is a confirmed real one, taken from themes saved by
/// ScreenKeyWindows itself. Note the vendor's own naming is inconsistent - some icons carry
/// their pixel dimensions, some a trailing underscore - which is precisely why callers should
/// not have to type them.
/// </summary>
public enum DeviceIcon
{
    /// <summary>Page-switch arrows, key sized. Used by previous/next and absolute-jump keys.</summary>
    PageSwitch = 0,

    /// <summary>"Open folder" artwork, key sized. Used by <see cref="KeyActions.OpenPage"/> keys.</summary>
    OpenFolder = 1,

    /// <summary>"Return to previous level" arrow, key sized. Used by <see cref="KeyActions.OneLevelUp"/> keys.</summary>
    OneLevelUp = 2,

    /// <summary>Keyboard artwork, key sized - for a key that types a keystroke.</summary>
    Keyboard = 3,

    /// <summary>System-volume artwork, sized for an encoder's own display.</summary>
    EncoderSystemVolume = 4,

    /// <summary>Device-brightness artwork, sized for an encoder's own display.</summary>
    EncoderDeviceBrightness = 5,

    /// <summary>Device-volume artwork (the device's own speaker, not the PC's), sized for an encoder's display.</summary>
    EncoderDeviceVolume = 6,

    /// <summary>System-media artwork, sized for an encoder's own display.</summary>
    EncoderSystemMedia = 7,

    /// <summary>Keyboard artwork, sized for an encoder's own display - what a vendor-saved <c>encoder_keyboard</c> key uses.</summary>
    EncoderKeyboard = 8,
}

/// <summary>
/// Resolves a <see cref="DeviceIcon"/> to the device-side path it lives at. Kept separate from
/// the enum so the paths stay in exactly one place.
/// </summary>
public static class DeviceIcons
{
    /// <summary>The confirmed device-side path for <paramref name="icon"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a value outside the enum.</exception>
    public static string PathOf(DeviceIcon icon) => icon switch
    {
        DeviceIcon.PageSwitch => "/static/icon/dark/PageSwitch_128x128.png",
        DeviceIcon.OpenFolder => "/static/icon/dark/createFolder_128x128.png",
        DeviceIcon.OneLevelUp => "/static/icon/dark/oneLevelUp_128x128.png",
        DeviceIcon.Keyboard => "/static/icon/dark/keyboard_128x128.png",
        DeviceIcon.EncoderSystemVolume => "/static/icon/white/systemVolume_.png",
        DeviceIcon.EncoderDeviceBrightness => "/static/icon/white/deviceBrightness_.png",
        DeviceIcon.EncoderDeviceVolume => "/static/icon/white/deviceVolume_.png",
        DeviceIcon.EncoderSystemMedia => "/static/icon/white/systemMedia_214x142.png",
        DeviceIcon.EncoderKeyboard => "/static/icon/white/keyboard_214x142.png",
        _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, "Unknown DeviceIcon."),
    };

    /// <summary>
    /// The icon that matches an encoder's built-in function, so a key bound to
    /// <see cref="EncoderFunctionType.SystemVolume"/> shows the volume artwork without the
    /// caller having to pair them up by hand.
    /// </summary>
    public static DeviceIcon ForEncoderFunction(EncoderFunctionType type) => type switch
    {
        EncoderFunctionType.SystemVolume => DeviceIcon.EncoderSystemVolume,
        EncoderFunctionType.DeviceBrightness => DeviceIcon.EncoderDeviceBrightness,
        EncoderFunctionType.DeviceVolume => DeviceIcon.EncoderDeviceVolume,
        EncoderFunctionType.SystemMedia => DeviceIcon.EncoderSystemMedia,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown EncoderFunctionType."),
    };
}
