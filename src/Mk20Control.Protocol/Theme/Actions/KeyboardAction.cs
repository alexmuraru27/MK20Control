namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// A key assigned to emit a keyboard keystroke ("type": "keyboard"). <see cref="Keycode"/>
/// matches the USB HID keyboard usage table (e.g. 4='A', 5='B', ... confirmed by decoding
/// real captured key remaps) for standard keys, and the HID modifier-key usage range
/// (224-231, e.g. 230="Right Alt") for modifier-only keys.
/// </summary>
public sealed record KeyboardAction : KeyAction
{
    /// <summary>The USB HID keyboard usage code.</summary>
    public required int Keycode { get; init; }

    /// <summary>The human-readable label shown in the editor (e.g. "A", "Enter", "Right Alt").</summary>
    public string? KeyLabel { get; init; }
}
