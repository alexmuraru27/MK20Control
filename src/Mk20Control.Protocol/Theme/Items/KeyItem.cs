using Mk20Control.Protocol.Theme.Actions;

namespace Mk20Control.Protocol.Theme.Items;

/// <summary>
/// A physical key item ("type": "115" in the theme layout JSON) - this is the primary
/// surface for "what does key N do" (keymap, icon, and assigned action).
/// </summary>
public sealed record KeyItem : ThemeItem
{
    /// <summary>Zero-based matrix row.</summary>
    public required int Row { get; init; }

    /// <summary>Zero-based matrix column.</summary>
    public required int Column { get; init; }

    /// <summary>
    /// The referenced icon asset's path, matching a <see cref="ThemeAsset.Path"/> entry in
    /// the same theme file (e.g. "/image/MK20-PLUS/cache/时尚/A.png"); null if this key has
    /// no custom icon.
    /// </summary>
    public string? IconAssetPath { get; init; }

    /// <summary>
    /// The key's assigned action, decoded from "controlData"; null if "controlData" was
    /// absent, empty, or could not be decoded as a tagged-value map (in which case
    /// <see cref="RawControlDataBase64"/> still preserves the original data).
    /// </summary>
    public KeyAction? Action { get; init; }

    /// <summary>The original base64-encoded "controlData" string, if present.</summary>
    public string? RawControlDataBase64 { get; init; }
}
