namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// A key assigned to open a specific page ("type": "openPage") - distinct from
/// <see cref="PageSwitchAction"/> (relative previous/next navigation): this jumps directly
/// to the page whose id is <see cref="PageName"/>. Confirmed present on
/// <c>defaultTheme.Theme</c>'s "create folder" keys (description "创建文件夹" = "Create
/// folder"), each targeting a different page GUID.
/// </summary>
public sealed record OpenPageAction : KeyAction
{
    /// <summary>The target page's id (a GUID matching a <c>ThemePage.PageName</c>), or the sentinel "parentPage" used by <see cref="OneLevelUpAction"/>-style navigation.</summary>
    public required string PageName { get; init; }
}

/// <summary>
/// A key assigned to navigate back up to the parent page ("type": "oneLevelUp").
/// Confirmed present on <c>defaultTheme.Theme</c> (description "返回到上一层" = "Return to
/// the previous level"); always observed with <c>pageName = "parentPage"</c>, a fixed
/// sentinel rather than a real page id.
/// </summary>
public sealed record OneLevelUpAction : KeyAction
{
    /// <summary>Always observed as the literal sentinel "parentPage", not a real page id.</summary>
    public string? PageName { get; init; }
}
