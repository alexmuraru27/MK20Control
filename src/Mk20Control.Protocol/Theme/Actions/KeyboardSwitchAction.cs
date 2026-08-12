namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// A key assigned to toggle/switch the active keyboard layout ("type": "keyboard_switch").
/// Confirmed present on a real retail theme's Caps Lock key
/// (description "键盘（切换）" = "Keyboard (switch)"); no additional operational fields
/// beyond the common <see cref="KeyAction"/> base were observed - this action's effect
/// appears to be entirely described by its presence/description, not extra parameters.
/// </summary>
public sealed record KeyboardSwitchAction : KeyAction;
