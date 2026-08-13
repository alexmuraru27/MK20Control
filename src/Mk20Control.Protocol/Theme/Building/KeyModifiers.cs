using System;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// USB HID keyboard-report modifier bitmask, as packed into the upper byte of a
/// <see cref="Actions.KeyboardAction.Keycode"/> combo (see <see cref="KeyActions.KeyboardCombo"/>).
/// Bit values match the standard USB HID Boot Keyboard modifier byte layout; only
/// <see cref="LeftCtrl"/>/<see cref="LeftAlt"/> (bits 0/2) are directly confirmed against a
/// real device capture so far (the Ctrl+Alt+Del combo) - the remaining bits follow the same
/// well-known USB HID standard but have not each been individually verified against this
/// specific device.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,

    /// <summary>Confirmed via a real capture (Ctrl+Alt+Del): bit 0 of the modifier byte.</summary>
    LeftCtrl = 1 << 0,
    LeftShift = 1 << 1,

    /// <summary>Confirmed via a real capture (Ctrl+Alt+Del): bit 2 of the modifier byte.</summary>
    LeftAlt = 1 << 2,
    LeftWin = 1 << 3,
    RightCtrl = 1 << 4,
    RightShift = 1 << 5,
    RightAlt = 1 << 6,
    RightWin = 1 << 7,
}
