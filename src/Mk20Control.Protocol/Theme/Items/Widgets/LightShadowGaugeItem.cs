namespace Mk20Control.Protocol.Theme.Items.Widgets;

/// <summary>
/// A "light-shadow" circular gauge item ("type": "110" in the theme layout JSON) - a
/// data-bound ring with a distinct arc-stroke style (separate <c>arcColor</c>/<c>arcWidth</c>
/// from the fill <c>back_color</c>) plus a glow/shadow highlight (<c>lightShadowColor</c>,
/// <c>lightShadowLighter</c>, <c>lightShadowPosition</c>) and direction flags
/// (<c>Clockwise</c>, <c>DisplayDirection</c>). Confirmed present in
/// <c>widgetThemeDemo.Theme</c> bound to "xiaozhiAIText" (an arbitrary custom
/// system_data_name in that sample - see PROTOCOL_WAVESHARE_MK20.md §10 Open Item #15).
/// </summary>
public sealed record LightShadowGaugeItem : ThemeItem
{
    public string? SystemDataName { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }

    /// <summary>Fill/track solid color, "r=...,g=...,b=...,a=..." format.</summary>
    public string? BackColor { get; init; }

    /// <summary>Arc stroke color, "r=...,g=...,b=...,a=..." format.</summary>
    public string? ArcColor { get; init; }

    public double? ArcWidth { get; init; }
    public double? Radius { get; init; }

    public bool? Clockwise { get; init; }

    /// <summary>Raw "DisplayDirection" flag (0/1); exact meaning not confirmed beyond its presence.</summary>
    public double? DisplayDirection { get; init; }

    /// <summary>Highlight/glow color, "r=...,g=...,b=...,a=..." format.</summary>
    public string? LightShadowColor { get; init; }

    public double? LightShadowLighter { get; init; }
    public double? LightShadowPosition { get; init; }
}
