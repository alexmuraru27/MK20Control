namespace Mk20Control.Protocol.Theme.Items;

/// <summary>
/// A radial/arc-style gauge item ("type": "109" in the theme layout JSON) - a data-bound
/// visual similar to <see cref="ProgressBarItem"/> (type "102") but rendered as an arc with
/// a configurable angle range and up to three gradient colors, confirmed present in
/// <c>defaultTheme.Theme</c> bound to "CPU Usage"/"GPU Usage"/"RAM Usage".
/// </summary>
public sealed record RadialGaugeItem : ThemeItem
{
    /// <summary>The data-source key this gauge is bound to (e.g. "CPU Usage"), when "system_data_flag" is "1"; null if not data-bound.</summary>
    public string? SystemDataName { get; init; }

    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }

    /// <summary>The arc's start angle in degrees (e.g. 225).</summary>
    public double? AngleMinValue { get; init; }

    /// <summary>The arc's end angle in degrees (e.g. 315).</summary>
    public double? AngleMaxValue { get; init; }

    public double? ArcRadius { get; init; }
    public double? ArcCircularInterval { get; init; }

    /// <summary>Gradient stop colors, each as the original "r=...,g=...,b=...,a=..." string; null entries mean that stop wasn't present.</summary>
    public string? GradientColor1 { get; init; }
    public string? GradientColor2 { get; init; }
    public string? GradientColor3 { get; init; }
}
