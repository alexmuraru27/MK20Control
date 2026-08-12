namespace Mk20Control.Protocol.Theme.Items;

/// <summary>
/// A linear bar-with-border gauge item ("type": "103" in the theme layout JSON) - similar
/// in purpose to <see cref="ProgressBarItem"/> (type "102") and <see cref="RadialGaugeItem"/>
/// (type "109") but styled with a solid front/back/border color set instead of a gradient.
/// Confirmed present in <c>defaultTheme.Theme</c> bound to a memory-usage data source
/// (observed with the Chinese data-source name "内存利用率" = "Memory Utilization Rate").
/// </summary>
public sealed record LinearGaugeItem : ThemeItem
{
    /// <summary>The data-source key this gauge is bound to, when "system_data_flag" is "1"; null if not data-bound.</summary>
    public string? SystemDataName { get; init; }

    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }

    /// <summary>Colors as the original "r=...,g=...,b=...,a=..." strings.</summary>
    public string? FrontColor { get; init; }
    public string? BackColor { get; init; }
    public string? BorderColor { get; init; }
    public double? BorderWidth { get; init; }
}
