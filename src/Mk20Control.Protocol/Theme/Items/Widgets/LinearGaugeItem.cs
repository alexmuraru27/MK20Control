namespace Mk20Control.Protocol.Theme.Items.Widgets;

/// <summary>
/// A linear bar-with-border gauge item ("type": "103" in the theme layout JSON) - the
/// rectangular/segmented horizontal bar the ScreenKeyWindows editor calls <b>"seg-hor"</b>.
///
/// The editor offers two horizontal bars, and they are separate item types rather than a
/// style flag on one: <see cref="ProgressBarItem"/> (type "102") has rounded ends and
/// supports a linear-gradient fill, whereas this type carries neither
/// <c>corner_radius</c> nor the <c>lineargradient_*</c> pair and is drawn with a solid
/// front/back/border color set.
///
/// CONFIRMED 2026-08-14: a "seg-hor" bar authored in ScreenKeyWindows saved as a plain
/// type-103 item whose field set matches what this library emits - it round-tripped
/// byte-identically through <c>ThemeFileCodec</c> and rendered correctly on real hardware.
/// Also confirmed present in <c>defaultTheme.Theme</c> bound to a memory-usage data source
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
