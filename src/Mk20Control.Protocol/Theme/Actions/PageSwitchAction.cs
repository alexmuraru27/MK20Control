namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// A key assigned to switch the device's active page ("type": "pageSwitch").
/// <see cref="PageSwitchMode"/> was observed as 1 for "previous page" and 2 for "next page"
/// on real hardware; a value of 0 was also observed in a device-to-host echo whose exact
/// meaning is unconfirmed - the raw integer is preserved rather than mapped to an assumed enum.
/// </summary>
public sealed record PageSwitchAction : KeyAction
{
    public required int PageSwitchMode { get; init; }
    public int JumpToPage { get; init; }
}
