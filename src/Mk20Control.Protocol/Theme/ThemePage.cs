using System.Collections.Generic;
using System.Text.Json;
using Mk20Control.Protocol.Theme.Items;

namespace Mk20Control.Protocol.Theme;

/// <summary>A single page within a theme ("pages" array entry in the layout JSON).</summary>
public sealed record ThemePage
{
    /// <summary>The page's unique identifier (a GUID string in observed themes).</summary>
    public string? PageName { get; init; }

    /// <summary>
    /// The <see cref="PageName"/> of this page's parent, present ONLY on "folder" sub-pages
    /// (absent entirely on ordinary top-level pages). This is what actually makes a page a
    /// folder: a <c>oneLevelUp</c> key's <c>pageName="parentPage"</c> is a sentinel meaning
    /// "go to my page's <c>parentPageName</c>", so a page opened via <c>openPage</c> but
    /// lacking this field is a normal page the device will happily navigate INTO and then
    /// refuse to leave (the return key is received and decoded, but does nothing).
    ///
    /// Confirmed by editing a builder-made theme in ScreenKeyWindows: the vendor added a new
    /// page carrying <c>parentPageName</c> pointing at the page whose key opens it, and by a
    /// real five-level nested-folder theme where each level's <c>parentPageName</c> names the
    /// level above it.
    /// </summary>
    public string? ParentPageName { get; init; }

    public required ThemeCanvas Canvas { get; init; }

    public required IReadOnlyList<ThemeItem> Items { get; init; }

    /// <summary>
    /// The page-level "encoder" array's raw JSON, describing the device's physical rotary
    /// encoder hardware (confirmed present on every real top-level/main-screen theme file
    /// examined - "row":103/104 entries with keycode/keyString fields - but absent from
    /// secondary/sub-page theme files under Key/, SecondaryScreen/, Encoder/relatedTheme/).
    /// Preserved verbatim (not yet individually modeled) so round-tripping a real theme, or
    /// building/editing a main-screen theme via <c>ThemeBuilder</c>/<c>ThemeEditor</c>, never
    /// silently drops this field - its complete absence was confirmed to make ScreenKeyWindows
    /// itself lock up when loading an otherwise-valid theme file (see
    /// PROTOCOL_WAVESHARE_MK20.md §10 Item #10).
    /// </summary>
    public JsonElement? Encoder { get; init; }
}
