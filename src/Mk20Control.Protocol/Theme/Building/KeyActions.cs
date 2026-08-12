using System.Collections.Generic;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Ergonomic factory methods for every confirmed <see cref="KeyAction"/> variant, for use
/// with <see cref="ThemeBuilder"/>/<see cref="ThemeEditor"/> when programmatically assigning
/// a key's behavior (keystroke, URL, mouse click, page navigation, text injection, audio
/// volume, encoder function, ...). Every action produced here starts from an empty
/// <c>RawFields</c> map (safe for a brand-new key) - see <c>ThemeFileCodec.EncodeKeyAction</c>
/// for exactly which JSON/tagged-value fields each variant serializes.
/// </summary>
public static class KeyActions
{
    private static readonly Dictionary<string, TaggedValue> Empty = new();

    /// <summary>Emits a keyboard keystroke. <paramref name="keycode"/> is a USB HID keyboard usage code (e.g. 4='A', 0x1E=30='1').</summary>
    public static KeyboardAction Keyboard(int keycode, string? keyLabel = null, string? description = null) => new()
    {
        RawType = "keyboard",
        Description = description,
        RawFields = Empty,
        Keycode = keycode,
        KeyLabel = keyLabel,
    };

    /// <summary>Opens a URL in the host's default browser.</summary>
    public static OpenWebAction OpenWeb(string url, string? description = null) => new()
    {
        RawType = "openWeb",
        Description = description,
        RawFields = Empty,
        Url = url,
    };

    /// <summary>Performs a mouse click, move, or scroll. See <see cref="MouseAction"/> remarks - raw integers are not individually enumerated/confirmed for every option.</summary>
    public static MouseAction Mouse(int mouseKey, int mouseEvent, int x = 0, int y = 0, int verticalScroll = 0, int horizontalScroll = 0, string? description = null) => new()
    {
        RawType = "qmk_mouse",
        Description = description,
        RawFields = Empty,
        MouseKey = mouseKey,
        MouseEvent = mouseEvent,
        MouseX = x,
        MouseY = y,
        MouseVerticalScroll = verticalScroll,
        MouseHorizontalScroll = horizontalScroll,
    };

    /// <summary>Navigates to the previous page on the current level (confirmed <c>pageSwitchMode=1</c>).</summary>
    public static PageSwitchAction PreviousPage(string? description = null) => new()
    {
        RawType = "pageSwitch",
        Description = description,
        RawFields = Empty,
        PageSwitchMode = 1,
    };

    /// <summary>Navigates to the next page on the current level (confirmed <c>pageSwitchMode=2</c>).</summary>
    public static PageSwitchAction NextPage(string? description = null) => new()
    {
        RawType = "pageSwitch",
        Description = description,
        RawFields = Empty,
        PageSwitchMode = 2,
    };

    /// <summary>Jumps directly to the page whose id is <paramref name="pageName"/> (a <see cref="ThemePage.PageName"/> GUID) - e.g. entering a "folder" of keys.</summary>
    public static OpenPageAction OpenPage(string pageName, string? description = null) => new()
    {
        RawType = "openPage",
        Description = description,
        RawFields = Empty,
        PageName = pageName,
    };

    /// <summary>Navigates back up to the parent page (always uses the fixed sentinel "parentPage", not a real page id).</summary>
    public static OneLevelUpAction OneLevelUp(string? description = null) => new()
    {
        RawType = "oneLevelUp",
        Description = description,
        RawFields = Empty,
        PageName = "parentPage",
    };

    /// <summary>Types literal text into the host, optionally pressing Enter afterward or using clipboard paste instead of keystrokes.</summary>
    public static TextInputAction TypeText(string text, bool pressEnterAfter = false, bool useCopyPaste = false, string? description = null) => new()
    {
        RawType = "text",
        Description = description,
        RawFields = Empty,
        InputText = text,
        IsInputEnter = pressEnterAfter,
        IsCopyPaste = useCopyPaste,
    };

    /// <summary>Adjusts the volume of a specific, named OS audio device (recording or playback).</summary>
    public static AudioVolumeAction AudioVolume(AudioDeviceClass deviceClass, string targetDeviceName, int adjustMode, int adjustValue, bool switchDefaultDevice = false, string? description = null) => new()
    {
        RawType = deviceClass == AudioDeviceClass.Microphone ? "Microphone" : "Loudspeaker",
        Description = description,
        RawFields = Empty,
        DeviceClass = deviceClass,
        TargetDeviceName = targetDeviceName,
        VolumeAdjustMode = adjustMode,
        VolumeAdjustValue = adjustValue,
        IsSwitchDefaultDevice = switchDefaultDevice,
    };

    /// <summary>Toggles/switches the active keyboard layout - no extra operational fields beyond the common base.</summary>
    public static KeyboardSwitchAction KeyboardSwitch(string? description = null) => new()
    {
        RawType = "keyboard_switch",
        Description = description,
        RawFields = Empty,
    };

    /// <summary>Binds the rotary encoder's three physical actions (rotate left, click, rotate right) each to a keystroke - e.g. volume down/mute/up.</summary>
    public static EncoderKeyboardAction EncoderKeyboard(
        int leftKeycode, string? leftKeyLabel,
        int middleKeycode, string? middleKeyLabel,
        int rightKeycode, string? rightKeyLabel,
        string? description = null) => new()
    {
        RawType = "encoder_keyboard",
        Description = description,
        RawFields = Empty,
        Category = "encoder",
        LeftKeycode = leftKeycode,
        LeftKeyLabel = leftKeyLabel,
        MiddleKeycode = middleKeycode,
        MiddleKeyLabel = middleKeyLabel,
        RightKeycode = rightKeycode,
        RightKeyLabel = rightKeyLabel,
    };

    /// <summary>
    /// A built-in encoder function - "encoder_system_volume", "encoder_system_media", or
    /// "encoder_device_brightness". The volume/brightness variants optionally reference a
    /// separate <c>.Theme</c> file shown on the encoder's small display while active.
    /// </summary>
    public static EncoderFunctionAction EncoderFunction(string rawType, string? relatedThemePath = null, string? description = null) => new()
    {
        RawType = rawType,
        Description = description,
        RawFields = Empty,
        Category = "encoder",
        RelatedThemePath = relatedThemePath,
    };
}
