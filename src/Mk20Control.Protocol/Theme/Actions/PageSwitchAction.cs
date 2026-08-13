namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// A key assigned to switch the device's active page ("type": "pageSwitch").
/// <see cref="PageSwitchMode"/> is confirmed as 1 = "previous page" and 2 = "next page"
/// (both RELATIVE to the currently shown page), and 0 = "jump to the page at index
/// <see cref="JumpToPage"/>" (ABSOLUTE).
///
/// Mode 0 is confirmed against the vendor's own <c>defaultTheme.Theme</c>, which navigates
/// exclusively this way: its home page carries keys with <c>jumpToPage</c> 1/2/3 and each of
/// those pages carries a <c>jumpToPage=0</c> key to return home. Build one with
/// <see cref="Building.KeyActions.JumpToPage"/>.
/// </summary>
public sealed record PageSwitchAction : KeyAction
{
    /// <summary>1 = previous page, 2 = next page (both relative); 0 = absolute jump to <see cref="JumpToPage"/>.</summary>
    public required int PageSwitchMode { get; init; }

    /// <summary>Zero-based index into the theme's <c>pages</c> array, used when <see cref="PageSwitchMode"/> is 0. Not a <c>PageName</c> GUID - see <see cref="OpenPageAction"/> for GUID-targeted navigation.</summary>
    public int JumpToPage { get; init; }
}
