using System.Text.Json;

namespace Mk20Control.Protocol.Theme.Items;

/// <summary>
/// Base type for a single visual element on a theme page ("items" array entry in the
/// embedded theme layout JSON). Confirmed common fields are exposed as strongly-typed
/// properties; <see cref="RawJson"/> always retains the complete original JSON element so
/// no data is ever lost for fields this library doesn't yet model.
///
/// Note: in the real theme JSON, numeric-looking fields (x/y/z/w/h/rotate/scale/id/...) are
/// serialized as JSON *strings* (e.g. <c>"x": "138"</c>), not JSON numbers. The properties
/// below parse them on a best-effort basis (null if absent or unparsable) - always fall back
/// to <see cref="RawJson"/> if a value is unexpectedly missing.
/// </summary>
public abstract record ThemeItem
{
    /// <summary>The original "type" field value, e.g. "100", "102", "113", "114", "115".</summary>
    public required string RawTypeCode { get; init; }

    public string? Id { get; init; }
    public string? ItemName { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Z { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? Rotate { get; init; }
    public double? Scale { get; init; }
    public bool? IsLocked { get; init; }

    /// <summary>The complete, original JSON element for this item - always present, never lossy.</summary>
    public required JsonElement RawJson { get; init; }
}
