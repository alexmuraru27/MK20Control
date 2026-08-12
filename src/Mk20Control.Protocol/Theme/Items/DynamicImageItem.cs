namespace Mk20Control.Protocol.Theme.Items;

/// <summary>
/// A "Dynamic Image" item ("type": "114" in the theme layout JSON) - confirmed to be used
/// for animated GIFs (referenced by <see cref="AssetPath"/>). Optionally bound to a live
/// data source, though this combination has not been separately confirmed against hardware.
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
}
