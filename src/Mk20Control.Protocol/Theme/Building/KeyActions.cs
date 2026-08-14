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

    /// <summary>
    /// Jumps directly to the page at zero-based index <paramref name="pageIndex"/> in the
    /// theme's <c>pages</c> array - an ABSOLUTE jump, unlike <see cref="PreviousPage"/>/
    /// <see cref="NextPage"/>, which are relative to whichever page is currently shown.
    ///
    /// Confirmed against the vendor's own <c>defaultTheme.Theme</c>, which uses this
    /// exclusively (it contains no relative page-switch keys at all): its home page carries
    /// three keys with <c>pageSwitchMode=0</c> and <c>jumpToPage</c> 1/2/3, and each of those
    /// three destination pages carries a bottom-right key with <c>jumpToPage=0</c> to return
    /// home - i.e. a hub-and-spoke menu. Note <c>jumpToPage</c> is a page INDEX, not a
    /// <see cref="ThemePage.PageName"/> GUID (that is <see cref="OpenPage"/>'s job).
    /// </summary>
    public static PageSwitchAction JumpToPage(int pageIndex, string? description = null) => new()
    {
        RawType = "pageSwitch",
        Description = description ?? "Page switching",
        ParentDescription = "Page switching",
        IconPath = "/static/icon/dark/PageSwitch.png",
        RawFields = new Dictionary<string, TaggedValue> { ["AISoundControlKeyword"] = TaggedValue.Of("") },
        PageSwitchMode = 0,
        JumpToPage = pageIndex,
    };

    /// <summary>
    /// Enters a "folder": jumps directly to the page whose id is <paramref name="pageName"/>
    /// (a <see cref="ThemePage.PageName"/> GUID, e.g. another page builder's
    /// <c>PageId</c>). Pair with <see cref="OneLevelUp"/> on the target page to get back.
    ///
    /// Confirmed against real vendor themes (<c>defaultTheme.Theme</c> and a folder-structure
    /// theme nested five levels deep): folder keys carry <c>parentDescription</c>
    /// "Page switching" and <c>iconPath</c> "/static/icon/dark/createFolder.png". Folders are
    /// NOT nested in the file - every page lives in the same flat <c>pages</c> array and a
    /// "folder" is simply a page that some key opens.
    /// </summary>
    public static OpenPageAction OpenPage(string pageName, string? description = null) => new()
    {
        RawType = "openPage",
        Description = description ?? "Create folders",
        ParentDescription = "Page switching",
        IconPath = "/static/icon/dark/createFolder.png",
        // Field ORDER is preserved through encoding (the codec seeds its map from RawFields
        // and overwrites in place), so seed the exact order a real ScreenKeyWindows-written
        // key uses: type, parentDescription, pageName, iconPath, description,
        // AISoundControlKeyword.
        RawFields = NavigationRawFields("openPage", pageName, "/static/icon/dark/createFolder.png", description ?? "Create folders"),
        PageName = pageName,
    };

    /// <summary>
    /// Navigates back up out of a "folder" to the page it was opened from. Always uses the
    /// fixed sentinel <c>pageName="parentPage"</c>, never a real page id - confirmed even
    /// five levels deep in a real nested-folder theme, so the device pops a runtime
    /// navigation stack rather than reading a parent declared in the file.
    ///
    /// Confirmed metadata: <c>parentDescription</c> "Page switching" and <c>iconPath</c>
    /// "/static/icon/dark/oneLevelUp.png". Real themes consistently place this key at the
    /// bottom-right cell (row 3, column 4) of a folder page.
    /// </summary>
    public static OneLevelUpAction OneLevelUp(string? description = null) => new()
    {
        RawType = "oneLevelUp",
        Description = description ?? "Return to the previous level",
        ParentDescription = "Page switching",
        IconPath = "/static/icon/dark/oneLevelUp.png",
        RawFields = NavigationRawFields("oneLevelUp", "parentPage", "/static/icon/dark/oneLevelUp.png", description ?? "Return to the previous level"),
        PageName = "parentPage",
    };

    /// <summary>
    /// Builds a folder-navigation action's field map in the exact order a real
    /// ScreenKeyWindows-written key uses. Dictionary insertion order survives encoding, and
    /// the codec overwrites existing keys in place rather than appending, so seeding these
    /// here makes a builder-made key byte-order-identical to a vendor one.
    /// </summary>
    private static Dictionary<string, TaggedValue> NavigationRawFields(
        string type, string pageName, string iconPath, string description) => new()
    {
        ["type"] = TaggedValue.Of(type),
        ["parentDescription"] = TaggedValue.Of("Page switching"),
        ["pageName"] = TaggedValue.Of(pageName),
        ["iconPath"] = TaggedValue.Of(iconPath),
        ["description"] = TaggedValue.Of(description),
        ["AISoundControlKeyword"] = TaggedValue.Of(""),
    };

    /// <summary>
    /// Assigns a caller-defined ID to a button, which the device echoes back on every press -
    /// the building block for "50 buttons across pages and folders, each running its own C#".
    /// The ID is private between your theme and your application; it is not a keystroke and
    /// has no meaning to the OS or to any game.
    ///
    /// <code>
    /// page.AddKey(0, 0, key => key.Icon(...).Title("PIT").Action(KeyActions.Command("pit.request")));
    /// // then: buttons.OnCommand("pit.request", () => sim.RequestPitStop());
    /// </code>
    ///
    /// WHY AN ID RATHER THAN row/column: the device's key event reports only
    /// <c>{row, col, pressed}</c> - it does NOT say which page the press came from (confirmed
    /// by decoding real captures). So r0c0 on page 1 and r0c0 inside a folder are
    /// indistinguishable by position. The ACTION DESCRIPTOR is echoed back per key, so an ID
    /// carried there is the only reliable way to tell 50 buttons apart - and it keeps working
    /// if you later move a button to a different cell or page.
    ///
    /// WHY AN ACTION AT ALL: the device fires a key event ONLY for keys with an action bound
    /// in the loaded theme. A key with no action produces no wire traffic whatsoever, and
    /// there is no generic "any key pressed" event (PROTOCOL_WAVESHARE_MK20.md §6.3).
    ///
    /// Implemented as a <c>text</c> action carrying the ID, because the device does not
    /// execute text keys itself: it reports the press with the string attached and emits
    /// no HID keystrokes of its own (confirmed by USB capture of the device's HID
    /// endpoint). Your handler receives the ID through <c>KeyBindings</c>.
    ///
    /// IMPORTANT - do not run the vendor app at the same time. ScreenKeyWindows also
    /// listens for these events, and it DOES act on a text key by typing the string.
    /// Confirmed on real hardware: with the vendor app running, pressing these keys types
    /// the raw command ids ("demo.hello", "demo.time", ...) into the focused window. The
    /// device is only reporting; whichever host application is listening decides what to
    /// do. Since the vendor app also holds the serial port exclusively, it must be closed
    /// for this library to connect anyway.
    /// </summary>
    /// <param name="commandId">The routing id - any string meaningful to your application, e.g. "pit.request". This is what <c>KeyBindings.OnCommand(id, ...)</c> matches on, so it must be unique per button; it is never displayed on the device.</param>
    /// <param name="description">An optional label echoed back on every press, so a handler or log can report the button's name rather than its grid position. Purely informational - NOT used for routing, and it does NOT change what the device draws (that is the key's own Title). Conventionally set to the same text as the title. Defaults to "Text", matching a vendor-written text key.</param>
    public static TextInputAction Command(string commandId, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        return TypeText(commandId, pressEnterAfter: false, useCopyPaste: false, description: description);
    }

    /// <summary>
    /// A raw <c>text</c> action carrying an arbitrary string - the low-level form behind
    /// <see cref="Command"/>, exposed for round-tripping vendor themes and for the
    /// <c>isInputEnter</c>/<c>isCopyPaste</c> flags the vendor editor sets.
    ///
    /// NOTHING TYPES THIS. The device emits zero HID keystrokes for a text key (confirmed by
    /// USB capture) and this library performs no OS input, so the string is only ever
    /// reported to your handler. The flags are preserved purely so vendor themes survive a
    /// decode/encode round trip; for your own themes prefer <see cref="Command"/>.
    ///
    /// Confirmed via a real capture (tools/Captures/capture7, a key assigned to text input in
    /// the vendor editor): real text keys carry <c>description":"Text"</c>,
    /// <c>parentDescription":"System input control"</c>,
    /// <c>iconPath":"/static/icon/dark/Text.png"</c> and an (empty)
    /// <c>AISoundControlKeyword</c> alongside the three operational fields.
    /// </summary>
    /// <param name="text">The string to carry.</param>
    /// <param name="pressEnterAfter">Vendor flag, preserved for round-tripping; this library does not act on it.</param>
    /// <param name="useCopyPaste">Vendor flag, preserved for round-tripping; this library does not act on it.</param>
    public static TextInputAction TypeText(string text, bool pressEnterAfter = false, bool useCopyPaste = false, string? description = null) => new()
    {
        RawType = "text",
        Description = description ?? "Text",
        ParentDescription = "System input control",
        IconPath = "/static/icon/dark/Text.png",
        // Seed the exact field order a real ScreenKeyWindows-written text key uses - insertion
        // order survives encoding, and the codec overwrites existing keys in place rather
        // than appending. Confirmed byte-for-byte against a vendor-saved text key.
        RawFields = new Dictionary<string, TaggedValue>
        {
            ["type"] = TaggedValue.Of("text"),
            ["parentDescription"] = TaggedValue.Of("System input control"),
            ["isInputEnter"] = TaggedValue.Of(pressEnterAfter),
            ["isCopyPaste"] = TaggedValue.Of(useCopyPaste),
            ["inputText"] = TaggedValue.Of(text),
            ["iconPath"] = TaggedValue.Of("/static/icon/dark/Text.png"),
            ["description"] = TaggedValue.Of(description ?? "Text"),
            ["AISoundControlKeyword"] = TaggedValue.Of(""),
        },
        InputText = text,
        IsInputEnter = pressEnterAfter,
        IsCopyPaste = useCopyPaste,
    };

    /// <summary>
    /// Binds the rotary encoder's three physical actions (rotate left, click, rotate right)
    /// each to a keystroke - e.g. volume down/mute/up.
    ///
    /// Seeds the exact field set and order a real vendor <c>encoder_keyboard</c> action
    /// carries, decoded from <c>defaultTheme.Theme</c>: <c>type</c>,
    /// <c>parentDescription</c>, <c>iconPath</c>, then RIGHT, MIDDLE and LEFT keycode/label
    /// pairs in that order, then <c>description</c> and <c>category</c>.
    ///
    /// Note the device executes this natively (it emits HID keystrokes) and reports NOTHING
    /// over the serial channel - confirmed on hardware - so it cannot be observed via
    /// <c>KeyBindings</c>. It is, however, the only way to distinguish rotation DIRECTION,
    /// since rotate-left and rotate-right carry different keycodes.
    /// </summary>
    public static EncoderKeyboardAction EncoderKeyboard(
        int leftKeycode, string? leftKeyLabel,
        int middleKeycode, string? middleKeyLabel,
        int rightKeycode, string? rightKeyLabel,
        string? description = null) => new()
    {
        RawType = "encoder_keyboard",
        Description = description ?? "Keyboard",
        ParentDescription = "Encoder",
        IconPath = "/static/icon/white/keyboard.png",
        RawFields = new Dictionary<string, TaggedValue>
        {
            ["type"] = TaggedValue.Of("encoder_keyboard"),
            ["parentDescription"] = TaggedValue.Of("Encoder"),
            ["iconPath"] = TaggedValue.Of("/static/icon/white/keyboard.png"),
            ["encoder_right_keycode"] = TaggedValue.Of(rightKeycode),
            ["encoder_right_keyString"] = TaggedValue.Of(rightKeyLabel ?? ""),
            ["encoder_middle_keycode"] = TaggedValue.Of(middleKeycode),
            ["encoder_middle_keyString"] = TaggedValue.Of(middleKeyLabel ?? ""),
            ["encoder_left_keycode"] = TaggedValue.Of(leftKeycode),
            ["encoder_left_keyString"] = TaggedValue.Of(leftKeyLabel ?? ""),
            ["description"] = TaggedValue.Of(description ?? "Keyboard"),
            ["category"] = TaggedValue.Of("encoder"),
        },
        Category = "encoder",
        LeftKeycode = leftKeycode,
        LeftKeyLabel = leftKeyLabel,
        MiddleKeycode = middleKeycode,
        MiddleKeyLabel = middleKeyLabel,
        RightKeycode = rightKeycode,
        RightKeyLabel = rightKeyLabel,
    };

    /// <summary>
    /// Binds each of the encoder's three motions to a keystroke WITH optional modifiers -
    /// e.g. rotate-left = Ctrl+Z, click = Ctrl+Shift+C, rotate-right = Ctrl+Y. Pass
    /// <c>null</c> for a motion you do not want bound, which emits keycode 0 and an empty
    /// label - exactly what ScreenKeyWindows writes for an unassigned slot.
    ///
    /// Confirmed by having the vendor app assign Ctrl+Shift+C to an encoder click and
    /// re-saving: the modifier bitmask is packed into the upper byte of the same keycode
    /// field a plain keystroke uses (<c>(modifiers &lt;&lt; 8) | key</c>, so Ctrl+Shift+C is
    /// <c>0x0306</c> = 774), and the label is written as <c>"L Ctrl L Shift C"</c>.
    /// </summary>
    public static EncoderKeyboardAction EncoderKeyboard(
        (KeyModifiers Modifiers, HidKey Key)? rotateLeft,
        (KeyModifiers Modifiers, HidKey Key)? click,
        (KeyModifiers Modifiers, HidKey Key)? rotateRight,
        string? description = null)
    {
        static (int Keycode, string Label) Slot((KeyModifiers Modifiers, HidKey Key)? binding) =>
            binding is { } b
                ? (((int)b.Modifiers << 8) | ((int)b.Key & 0xFF), DescribeEncoderCombo(b.Modifiers, b.Key))
                : (0, "");

        var (leftCode, leftLabel) = Slot(rotateLeft);
        var (middleCode, middleLabel) = Slot(click);
        var (rightCode, rightLabel) = Slot(rotateRight);

        return EncoderKeyboard(leftCode, leftLabel, middleCode, middleLabel, rightCode, rightLabel, description);
    }

    /// <summary>
    /// Formats an encoder keystroke label the way ScreenKeyWindows does - modifiers as
    /// <c>"L Ctrl"</c>/<c>"L Shift"</c>/... separated by spaces, then the key, e.g.
    /// <c>"L Ctrl L Shift C"</c> (confirmed from a vendor-saved theme).
    /// </summary>
    private static string DescribeEncoderCombo(KeyModifiers modifiers, HidKey key)
    {
        var parts = new List<string>();
        foreach (KeyModifiers flag in Enum.GetValues<KeyModifiers>())
        {
            if (flag == KeyModifiers.None || !modifiers.HasFlag(flag)) continue;
            parts.Add(flag switch
            {
                KeyModifiers.LeftCtrl => "L Ctrl",
                KeyModifiers.LeftShift => "L Shift",
                KeyModifiers.LeftAlt => "L Alt",
                KeyModifiers.LeftWin => "L Win",
                KeyModifiers.RightCtrl => "R Ctrl",
                KeyModifiers.RightShift => "R Shift",
                KeyModifiers.RightAlt => "R Alt",
                KeyModifiers.RightWin => "R Win",
                _ => flag.ToString(),
            });
        }
        parts.Add(key.ToString());
        return string.Join(" ", parts);
    }

    /// <summary>
    /// A built-in encoder function - "encoder_system_volume", "encoder_system_media", or
    /// "encoder_device_brightness". The volume/brightness variants optionally reference a
    /// separate <c>.Theme</c> file shown on the encoder's small display while active.
    /// Prefer the <see cref="EncoderFunctionType"/> overload below for compile-time-checked
    /// values; this raw-string overload remains for any future/unconfirmed function type.
    ///
    /// Seeds the exact field set and order a real vendor encoder action carries - confirmed
    /// by decoding <c>defaultTheme.Theme</c> and <c>海边吹风.Theme</c> and by a live
    /// DEVICE_ProactiveEscalationCMD capture: <c>type</c>, optional <c>relatedTheme</c>,
    /// <c>parentDescription</c>, <c>iconPath</c>, <c>description</c>, <c>category</c>.
    /// Emitting only <c>type</c>/<c>category</c> (as this factory previously did) produces an
    /// action no real theme resembles.
    /// </summary>
    public static EncoderFunctionAction EncoderFunction(string rawType, string? relatedThemePath = null, string? description = null)
    {
        var (defaultIconPath, defaultDescription) = EncoderFunctionMetadata(rawType);

        var fields = new Dictionary<string, TaggedValue> { ["type"] = TaggedValue.Of(rawType) };
        if (relatedThemePath is not null) fields["relatedTheme"] = TaggedValue.Of(relatedThemePath);
        fields["parentDescription"] = TaggedValue.Of("Encoder");
        fields["iconPath"] = TaggedValue.Of(defaultIconPath);
        fields["description"] = TaggedValue.Of(description ?? defaultDescription);
        fields["category"] = TaggedValue.Of("encoder");

        return new()
        {
            RawType = rawType,
            Description = description ?? defaultDescription,
            ParentDescription = "Encoder",
            IconPath = defaultIconPath,
            RawFields = fields,
            Category = "encoder",
            RelatedThemePath = relatedThemePath,
        };
    }

    /// <summary>The confirmed real <c>iconPath</c>/<c>description</c> a vendor encoder action carries for each function type.</summary>
    private static (string IconPath, string Description) EncoderFunctionMetadata(string rawType) => rawType switch
    {
        "encoder_system_volume" => ("/static/icon/white/systemVolume.png", "System volume"),
        "encoder_device_brightness" => ("/static/icon/white/deviceBrightness.png", "Device brightness"),
        "encoder_system_media" => ("/static/icon/white/systemMedia.png", "System audio"),
        "encoder_device_volume" => ("/static/icon/white/deviceVolume.png", "Device volume"),
        "encoder_keyboard" => ("/static/icon/white/keyboard.png", "Keyboard"),
        _ => ("/static/icon/white/systemVolume.png", "Encoder"),
    };

    /// <summary>
    /// A built-in encoder function, strongly typed via <see cref="EncoderFunctionType"/>
    /// instead of a raw string (e.g. <c>KeyActions.EncoderFunction(EncoderFunctionType.SystemVolume)</c>).
    /// </summary>
    public static EncoderFunctionAction EncoderFunction(EncoderFunctionType type, string? relatedThemePath = null, string? description = null)
        => EncoderFunction(type.ToRawType(), relatedThemePath, description);
}
