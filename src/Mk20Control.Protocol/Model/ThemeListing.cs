using System.Collections.Generic;

namespace Mk20Control.Protocol.Model;

/// <summary>One installed theme entry as reported by GET_DEVICE_THEME (a device-side file path and its CRC-32).</summary>
public sealed record InstalledTheme(string Path, uint Crc32);

/// <summary>
/// The decoded GET_DEVICE_THEME reply: device storage free-space fields plus the list of
/// installed themes. Confirmed wire format:
/// <see cref="Mk20Control.Protocol.Codecs.SimpleStringMapCodec"/> - a plain string/string
/// map (every value, even a CRC-32, is UTF-16BE decimal text), NOT the typeId-tagged
/// <see cref="Mk20Control.Protocol.Codecs.VariantMapCodec"/> format. Confirmed layout: a
/// "bytesTotal" and "bytesAvailable" entry, plus one additional entry PER installed theme
/// where the map KEY itself is the theme's device-side path and the VALUE is its CRC-32
/// rendered as decimal text (an unusual "path as dictionary key" shape, confirmed directly
/// from both capture and a live device).
/// </summary>
public sealed record ThemeListing
{
    /// <summary>
    /// Total theme storage, in MEGABYTES.
    ///
    /// The wire field is named "bytesTotal" but the unit is megabytes: a device with a 32 GB
    /// card reports 28003, i.e. ~27.3 GB. Verified against a live device whose installed
    /// themes (109 MB, including a 33 MB defaultTheme.Theme) match the 153 MB it reported as
    /// used. Reading this as bytes gives a nonsensical ~28 KB budget.
    /// </summary>
    public required long MegabytesTotal { get; init; }

    /// <summary>Free theme storage, in MEGABYTES - see <see cref="MegabytesTotal"/> for the unit.</summary>
    public required long MegabytesAvailable { get; init; }
    public required IReadOnlyList<InstalledTheme> Themes { get; init; }
}
