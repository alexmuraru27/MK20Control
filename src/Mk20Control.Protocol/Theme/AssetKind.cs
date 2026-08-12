namespace Mk20Control.Protocol.Theme;

/// <summary>The detected file kind of a <see cref="ThemeAsset"/>, determined from its magic bytes.</summary>
public enum AssetKind
{
    Unknown = 0,
    Png,
    Gif,
    Jpeg,
    /// <summary>Detected only by file extension (".mp4") in <see cref="ThemeAsset.Path"/> - MP4 has no simple fixed magic-byte check applied here.</summary>
    Mp4,
}
