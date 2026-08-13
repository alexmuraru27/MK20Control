namespace Mk20Control.Protocol.Theme.Items.Widgets;

/// <summary>
/// A simple circular gauge item ("type": "101" in the theme layout JSON) - a data-bound
/// solid-color ring/dial, simpler than <see cref="RadialGaugeItem"/> (type "109": angle
/// range + up to 3 gradient stops): only a single front/back color pair, a <see
/// cref="Margin"/> and <see cref="Radius"/>, no angle range or gradient. Confirmed present
/// in the MK10 variant's <c>defaultTheme.Theme</c> (3 instances bound to "Cpu使用率"/
/// "Gpu使用率"/"RAM使用率" - CPU/GPU/RAM usage); not observed in any MK20 sample theme.
/// </summary>
public sealed record CircularGaugeItem : ThemeItem
{
    /// <summary>The data-source key this gauge is bound to, when "system_data_flag" is "1"; null if not data-bound.</summary>
    public string? SystemDataName { get; init; }

    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }

    /// <summary>Solid fill color, "r=...,g=...,b=...,a=..." format.</summary>
    public string? FrontColor { get; init; }

    /// <summary>Solid track/background color, "r=...,g=...,b=...,a=..." format.</summary>
    public string? BackColor { get; init; }

    /// <summary>Gap between the dial's outer edge and its bounding box, in pixels.</summary>
    public double? Margin { get; init; }

    /// <summary>The dial's radius, in pixels.</summary>
    public double? Radius { get; init; }
}
