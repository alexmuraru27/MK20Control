using System.Collections.Generic;
using Mk20Control.Protocol.Theme.Items;

namespace Mk20Control.Protocol.Theme;

/// <summary>A single page within a theme ("pages" array entry in the layout JSON).</summary>
public sealed record ThemePage
{
    /// <summary>The page's unique identifier (a GUID string in observed themes).</summary>
    public string? PageName { get; init; }

    public required ThemeCanvas Canvas { get; init; }

    public required IReadOnlyList<ThemeItem> Items { get; init; }
}
