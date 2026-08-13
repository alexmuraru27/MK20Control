namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// A key assigned to emit a keyboard keystroke ("type": "keyboard"). <see cref="Keycode"/>
/// matches the USB HID keyboard usage table (e.g. 4='A', 5='B', ... confirmed by decoding
/// real captured key remaps) for standard keys, and the HID modifier-key usage range
/// (224-231, e.g. 230="Right Alt") for modifier-only keys.
///
/// <para>
/// <b>Modifier combos (e.g. Ctrl+Alt+Del):</b> confirmed via a real ScreenKeyWindows capture
/// (tools/Captures/capture18_ctrlaltdel_sanitized.pcapng - a genuine key assigned to
/// Ctrl+Alt+Del and saved through the vendor editor) that a combo is encoded as a single
/// 16-bit <see cref="Keycode"/> value: <c>(modifierBitmask &lt;&lt; 8) | baseHidKeycode</c>.
/// The captured value was <c>0x054C</c> = modifier byte <c>0x05</c> (bit0=Left Ctrl,
/// bit2=Left Alt) + base keycode <c>0x4C</c> (Delete) - i.e. exactly the standard USB HID
/// keyboard-report modifier-byte convention, packed into the upper byte of one field rather
/// than sent as a separate field. See <see cref="Building.KeyActions.KeyboardCombo"/> and
/// <see cref="Building.KeyActions.CtrlAltDel"/>.
/// </para>
/// </summary>
public sealed record KeyboardAction : KeyAction
{
    /// <summary>
    /// The USB HID keyboard usage code - for a plain keystroke, just the base code (e.g. 4='A').
    /// For a modifier combo, <c>(modifierBitmask &lt;&lt; 8) | baseHidKeycode</c> - see remarks.
    /// </summary>
    public required int Keycode { get; init; }

    /// <summary>The human-readable label shown in the editor (e.g. "A", "Enter", "Right Alt", "L Ctrl L Alt Del").</summary>
    public string? KeyLabel { get; init; }
}
