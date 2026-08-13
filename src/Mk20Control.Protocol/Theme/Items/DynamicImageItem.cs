namespace Mk20Control.Protocol.Theme.Items;

/// <summary>
/// A "Dynamic Image" item ("type": "114" in the theme layout JSON) - confirmed to be used
/// for animated GIFs (referenced by <see cref="AssetPath"/>). Optionally bound to a live
/// data source, though this combination has not been separately confirmed against hardware.
/// Also confirmed to be the real mechanism for embedding secondary-screen (428x142)
/// content directly inside a main-screen (640x656) theme page - see
/// <see cref="BackgroundType"/>.
/// </summary>
public sealed record DynamicImageItem : ThemeItem
{
    /// <summary>
    /// The referenced asset's path, matching a <see cref="ThemeAsset.Path"/> entry in the
    /// same theme file (e.g. "/theme/428x142/xxx.gif").
    /// </summary>
    public required string AssetPath { get; init; }

    /// <summary>The data-source key this image is bound to, when "system_data_flag" is "1"; otherwise null.</summary>
    public string? SystemDataName { get; init; }

    /// <summary>
    /// The item's "backgroundType" field, when present (e.g. "secondary" - confirmed via
    /// defaultTheme.Theme: a DynamicImageItem at x=106,y=0,w=428,h=142 with
    /// "backgroundType":"secondary" is how the secondary (2.8") screen's own background
    /// image/GIF is embedded inside a main-screen page). Null if the item has no such field
    /// (the common case for a plain decorative/data-bound image on the main screen).
    /// </summary>
    public string? BackgroundType { get; init; }
}
