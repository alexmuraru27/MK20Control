namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Standard USB HID keyboard/keypad usage codes (HID Usage Tables, page 0x07 "Keyboard/Keypad"),
/// for use with <see cref="KeyActions.Keyboard"/>/<see cref="KeyActions.KeyboardCombo"/>
/// instead of a raw magic-number <c>int</c>. Values match the well-known USB HID standard
/// table exactly (e.g. 4='A', 0x1E=30='1', 0x4C=76='Delete') - confirmed against this
/// device for the letters/digits (via decoded real theme key remaps, see
/// PROTOCOL_WAVESHARE_MK20.md §7.1) and for <see cref="Delete"/> specifically (via the
/// Ctrl+Alt+Del capture, tools/Captures/capture18_ctrlaltdel.pcapng). Keys not yet
/// individually confirmed against this device still use their standard HID code, since
/// every confirmed code so far has matched the standard table exactly with no deviation.
/// </summary>
public enum HidKey
{
    A = 0x04, B = 0x05, C = 0x06, D = 0x07, E = 0x08, F = 0x09, G = 0x0A, H = 0x0B,
    I = 0x0C, J = 0x0D, K = 0x0E, L = 0x0F, M = 0x10, N = 0x11, O = 0x12, P = 0x13,
    Q = 0x14, R = 0x15, S = 0x16, T = 0x17, U = 0x18, V = 0x19, W = 0x1A, X = 0x1B,
    Y = 0x1C, Z = 0x1D,

    /// <summary>Confirmed real key: matches the theme's own digit key #1 (see PROTOCOL_WAVESHARE_MK20.md §7).</summary>
    Digit1 = 0x1E,
    Digit2 = 0x1F,
    Digit3 = 0x20,
    Digit4 = 0x21,
    Digit5 = 0x22,
    Digit6 = 0x23,
    Digit7 = 0x24,
    Digit8 = 0x25,
    Digit9 = 0x26,
    Digit0 = 0x27,

    /// <summary>Confirmed real key (a plain, non-combo Enter key was observed in a real theme).</summary>
    Enter = 0x28,
    Escape = 0x29,
    Backspace = 0x2A,
    Tab = 0x2B,
    Space = 0x2C,
    Minus = 0x2D,
    Equals = 0x2E,
    LeftBracket = 0x2F,
    RightBracket = 0x30,
    Backslash = 0x31,
    Semicolon = 0x33,
    Apostrophe = 0x34,
    GraveAccent = 0x35,
    Comma = 0x36,
    Period = 0x37,
    Slash = 0x38,
    CapsLock = 0x39,

    F1 = 0x3A, F2 = 0x3B, F3 = 0x3C, F4 = 0x3D, F5 = 0x3E, F6 = 0x3F,
    F7 = 0x40, F8 = 0x41, F9 = 0x42, F10 = 0x43, F11 = 0x44, F12 = 0x45,

    PrintScreen = 0x46,
    ScrollLock = 0x47,
    Pause = 0x48,
    Insert = 0x49,
    Home = 0x4A,
    PageUp = 0x4B,

    /// <summary>Confirmed real key: the non-modifier part of a captured Ctrl+Alt+Del combo (tools/Captures/capture18_ctrlaltdel.pcapng).</summary>
    Delete = 0x4C,
    End = 0x4D,
    PageDown = 0x4E,
    RightArrow = 0x4F,
    LeftArrow = 0x50,
    DownArrow = 0x51,
    UpArrow = 0x52,

    NumLock = 0x53,
    KeypadDivide = 0x54,
    KeypadMultiply = 0x55,
    KeypadMinus = 0x56,
    KeypadPlus = 0x57,
    KeypadEnter = 0x58,
    Keypad1 = 0x59, Keypad2 = 0x5A, Keypad3 = 0x5B, Keypad4 = 0x5C, Keypad5 = 0x5D,
    Keypad6 = 0x5E, Keypad7 = 0x5F, Keypad8 = 0x60, Keypad9 = 0x61, Keypad0 = 0x62,
    KeypadPeriod = 0x63,

    Application = 0x65,

    /// <summary>Left Ctrl as a plain (non-held-modifier) keystroke - for a modifier held down while another key is pressed, use <see cref="KeyModifiers"/> + <see cref="KeyActions.KeyboardCombo"/> instead.</summary>
    LeftCtrl = 0xE0,
    LeftShift = 0xE1,
    LeftAlt = 0xE2,
    LeftWin = 0xE3,
    RightCtrl = 0xE4,
    RightShift = 0xE5,
    RightAlt = 0xE6,
    RightWin = 0xE7,
}
