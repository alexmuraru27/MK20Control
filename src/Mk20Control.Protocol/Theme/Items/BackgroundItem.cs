namespace Mk20Control.Protocol.Theme.Items;

/// <summary>
/// A background image or video item ("type": "100" in the theme layout JSON). Confirmed to
/// support at least PNG, GIF, and MP4 assets (referenced by <see cref="AssetPath"/>), for
/// both the main and secondary screens.
/// </summary>
public sealed record BackgroundItem : ThemeItem
{
    /// <summary>Which screen this background applies to.</summary>
    public required BackgroundSurface Surface { get; init; }

    /// <summary>The original "backgroundType" string value (e.g. "main", "secondary").</summary>
    public required string RawSurface { get; init; }

    /// <summary>
    /// The referenced asset's path, matching a <see cref="ThemeAsset.Path"/> entry in the
    /// same theme file (e.g. "/theme/MK20-PLUS/MainScreen/original-xxx.mp4").
    /// </summary>
    public required string AssetPath { get; init; }
}
