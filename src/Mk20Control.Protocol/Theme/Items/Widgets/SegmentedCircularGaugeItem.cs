namespace Mk20Control.Protocol.Theme.Items.Widgets;

/// <summary>
/// A segmented circular gauge item ("type": "104" in the theme layout JSON) - structurally
/// identical JSON field set to <see cref="CircularGaugeItem"/> (type "101": front/back solid
/// colors, margin, radius, no gradient/angle range) but rendered by the ScreenKeyWindows
/// editor as a distinct "seg-circular" widget (segmented/notched ring rather than a solid
/// arc). Confirmed present in <c>widgetThemeDemo.Theme</c> bound to "CPU Model" (an unusual
/// binding choice in that sample - any numeric-ish system_data_name works, see
/// PROTOCOL_WAVESHARE_MK20.md §10 Open Item #15). The two types cannot be told apart from
/// JSON fields alone; only the "type" code differs.
/// </summary>
public sealed record SegmentedCircularGaugeItem : ThemeItem
{
    public string? SystemDataName { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public string? FrontColor { get; init; }
    public string? BackColor { get; init; }
    public double? Margin { get; init; }
    public double? Radius { get; init; }
}
