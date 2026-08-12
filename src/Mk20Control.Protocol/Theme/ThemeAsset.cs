using System;

namespace Mk20Control.Protocol.Theme;

/// <summary>
/// A single embedded binary asset (image, animated GIF, or video) within a .Theme file,
/// referenced by <see cref="Path"/> from one or more <c>Items.ThemeItem</c>s (e.g. a
/// <c>BackgroundItem.AssetPath</c> or <c>KeyItem.IconAssetPath</c>).
/// </summary>
public sealed record ThemeAsset
{
    /// <summary>
    /// The internal virtual path used to reference this asset from theme items (e.g.
    /// "/image/428x142/PhotoAlbum/xxx.gif"). This is NOT necessarily a real path on the
    /// device's filesystem - it is only confirmed to be a key used within this same file.
    /// </summary>
    public required string Path { get; init; }

    public required byte[] Data { get; init; }

    /// <summary>The detected file kind, determined from magic bytes (or file extension for MP4).</summary>
    public AssetKind Kind => DetectKind(Path, Data);

    private static AssetKind DetectKind(string path, byte[] data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return AssetKind.Png;
        if (data.Length >= 3 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
            return AssetKind.Gif;
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
            return AssetKind.Jpeg;
        if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            return AssetKind.Mp4;
        return AssetKind.Unknown;
    }
}
