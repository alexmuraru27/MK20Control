using System.Collections.Generic;

namespace Mk20Control.Protocol.Theme;

/// <summary>
/// A fully decoded .Theme file: everything ScreenKeyWindows bundles when you edit keymaps,
/// icons, GIFs, backgrounds, encoder assignments, or sound/mouse/text actions and save -
/// see <c>Mk20Control.Protocol.Codecs.ThemeFileCodec</c> for the byte layout this represents.
/// </summary>
public sealed record ThemeFile
{
    /// <summary>The device-side language selector observed in the file header (integer code; exact meaning of each value not individually confirmed).</summary>
    public int Language { get; init; }

    /// <summary>
    /// Raw bytes of the "keyMacroValue" field observed in the file header - present in every
    /// theme file seen so far but its purpose is not understood; preserved for round-trip
    /// fidelity rather than discarded.
    /// </summary>
    public required byte[] KeyMacroValue { get; init; }

    /// <summary>
    /// Raw bytes of the "keyMacro" field observed in the file header - was null in every
    /// theme file seen so far; preserved (including null) for round-trip fidelity.
    /// </summary>
    public byte[]? KeyMacro { get; init; }

    /// <summary>The active page's identifier at save time ("main.currentPage" in the layout JSON).</summary>
    public required string CurrentPageId { get; init; }

    /// <summary>The layout format version string observed in the file (e.g. "V3.0").</summary>
    public required string LayoutVersion { get; init; }

    public required IReadOnlyList<ThemePage> Pages { get; init; }

    public required IReadOnlyList<ThemeAsset> Assets { get; init; }
}
