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
    public required long BytesTotal { get; init; }
    public required long BytesAvailable { get; init; }
    public required IReadOnlyList<InstalledTheme> Themes { get; init; }
}
