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

    /// <summary>Emits a keyboard keystroke. <paramref name="keycode"/> is a USB HID keyboard usage code (e.g. 4='A', 0x1E=30='1'). Prefer the <see cref="HidKey"/> overload for compile-time-checked key names.</summary>
    public static KeyboardAction Keyboard(int keycode, string? keyLabel = null, string? description = null) => new()
    {
        RawType = "keyboard",
        // Confirmed via --dump-raw-json against real hardware themes: every real KeyboardAction
        // carries "description":"Keyboard", "parentDescription":"System input control",
        // "iconPath":"/static/icon/dark/keyboard.png", and an (empty) "AISoundControlKeyword" -
        // omitting these (as this builder previously did, leaving them null/absent) produced a
        // structurally incomplete controlData blob compared to every real key observed.
        Description = description ?? "Keyboard",
        ParentDescription = "System input control",
        IconPath = "/static/icon/dark/keyboard.png",
        RawFields = new Dictionary<string, TaggedValue> { ["AISoundControlKeyword"] = TaggedValue.Of("") },
        Keycode = keycode,
        KeyLabel = keyLabel,
    };

    /// <summary>Emits a keyboard keystroke, strongly typed via <see cref="HidKey"/> instead of a raw USB HID integer code.</summary>
    public static KeyboardAction Keyboard(HidKey key, string? keyLabel = null, string? description = null) =>
        Keyboard((int)key, keyLabel ?? key.ToString(), description);

    /// <summary>
    /// Emits a keyboard keystroke with one or more held modifiers (e.g. Ctrl+Alt+Del,
    /// Ctrl+Shift+Esc, Alt+Tab), both fully strongly typed. Confirmed via a real
    /// ScreenKeyWindows capture (tools/Captures/capture18_ctrlaltdel.pcapng, a key assigned
    /// to Ctrl+Alt+Del in the vendor editor and saved): a modifier combo packs the USB HID
    /// keyboard-report modifier bitmask into the upper byte of the same <c>keycode</c>
    /// field a plain keystroke uses - <c>(modifiers &lt;&lt; 8) | baseKeycode</c> - rather
    /// than sending a separate modifier field. E.g.
    /// <c>KeyboardCombo(KeyModifiers.LeftCtrl | KeyModifiers.LeftAlt, HidKey.Delete)</c> for
    /// Ctrl+Alt+Del - confirmed byte-for-byte to encode as keycode <c>0x054C</c>.
    /// </summary>
    public static KeyboardAction KeyboardCombo(KeyModifiers modifiers, HidKey key, string? keyLabel = null, string? description = null) =>
        Keyboard(((int)modifiers << 8) | ((int)key & 0xFF), keyLabel ?? DescribeCombo(modifiers, key), description);

    /// <summary>Builds a human-readable default label for a modifier combo, e.g. "LeftCtrl+LeftAlt+Delete", used when no explicit <c>keyLabel</c> is supplied to <see cref="KeyboardCombo"/>.</summary>
    private static string DescribeCombo(KeyModifiers modifiers, HidKey key)
    {
        var parts = new List<string>();
        foreach (KeyModifiers flag in System.Enum.GetValues(typeof(KeyModifiers)))
        {
            if (flag != KeyModifiers.None && modifiers.HasFlag(flag))
                parts.Add(flag.ToString());
        }
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

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
        // Confirmed via a real DEVICE_ProactiveEscalationCMD key-press event
        // (PROTOCOL_WAVESHARE_MK20.md §9.4): real page-switch keys carry
        // parentDescription="Page switching", description="Page switching", and
        // iconPath="/static/icon/dark/PageSwitch.png".
        Description = description ?? "Page switching",
        ParentDescription = "Page switching",
        IconPath = "/static/icon/dark/PageSwitch.png",
        RawFields = new Dictionary<string, TaggedValue> { ["AISoundControlKeyword"] = TaggedValue.Of("") },
        PageSwitchMode = 1,
    };

    /// <summary>Navigates to the next page on the current level (confirmed <c>pageSwitchMode=2</c>).</summary>
    public static PageSwitchAction NextPage(string? description = null) => new()
    {
        RawType = "pageSwitch",
        Description = description ?? "Page switching",
        ParentDescription = "Page switching",
        IconPath = "/static/icon/dark/PageSwitch.png",
        RawFields = new Dictionary<string, TaggedValue> { ["AISoundControlKeyword"] = TaggedValue.Of("") },
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
    /// Prefer the <see cref="EncoderFunctionType"/> overload below for compile-time-checked
    /// values; this raw-string overload remains for any future/unconfirmed function type.
    /// </summary>
    public static EncoderFunctionAction EncoderFunction(string rawType, string? relatedThemePath = null, string? description = null) => new()
    {
        RawType = rawType,
        Description = description,
        RawFields = Empty,
        Category = "encoder",
        RelatedThemePath = relatedThemePath,
    };

    /// <summary>
    /// A built-in encoder function, strongly typed via <see cref="EncoderFunctionType"/>
    /// instead of a raw string (e.g. <c>KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)</c>).
    /// </summary>
    public static EncoderFunctionAction EncoderFunction(EncoderFunctionType type, string? relatedThemePath = null, string? description = null)
        => EncoderFunction(type.ToRawType(), relatedThemePath, description);
}
