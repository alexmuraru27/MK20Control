using System.Collections.Generic;

namespace Mk20Control.Protocol.Model;

/// <summary>
/// The device's identity/status information, as returned in a FIND_DEVICE reply. Confirmed
/// fields observed on real hardware: version, upgradeToLatestMethod, screen_width,
/// screen_model, screen_height, deviceVolume, deviceName, deviceBl. Confirmed wire format:
/// <see cref="Mk20Control.Protocol.Codecs.SimpleStringMapCodec"/> - a plain string/string
/// map (every value, even numeric-looking ones, is UTF-16BE text), NOT the typeId-tagged
/// <see cref="Mk20Control.Protocol.Codecs.VariantMapCodec"/> format used elsewhere in the
/// protocol.
/// </summary>
public sealed record DeviceIdentity
{
    public string? Version { get; init; }
    public int? UpgradeToLatestMethod { get; init; }
    public int? ScreenWidth { get; init; }
    public string? ScreenModel { get; init; }
    public int? ScreenHeight { get; init; }
    public int? DeviceVolume { get; init; }
    public string? DeviceName { get; init; }
    public int? DeviceBacklight { get; init; }

    /// <summary>The complete set of fields as decoded (raw strings), for access to anything not yet promoted to a strongly-typed property.</summary>
    public required IReadOnlyDictionary<string, string> RawFields { get; init; }
}
