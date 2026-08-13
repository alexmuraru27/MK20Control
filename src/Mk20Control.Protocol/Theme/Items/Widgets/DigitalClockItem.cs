namespace Mk20Control.Protocol.Theme.Items.Widgets;

/// <summary>
/// A live digital clock display item ("type": "111" in the theme layout JSON). Confirmed
/// present in <c>defaultTheme.Theme</c> as three adjacent items with
/// <c>system_data_name</c> "hour"/"minute"/"second" respectively (each rendering one
/// clock-field digit group) - i.e. a full clock is composed of multiple type-111 items
/// side by side, not one item with an internal hour:minute:second format string.
/// </summary>
public sealed record DigitalClockItem : ThemeItem
{
    /// <summary>Which clock field this item renders - confirmed values: "hour", "minute", "second".</summary>
    public string? SystemDataName { get; init; }

    public string? Font { get; init; }

    /// <summary>Colors as the original "r=...,g=...,b=...,a=..." strings.</summary>
    public string? FrontColor { get; init; }
    public string? BackColor { get; init; }
    public string? BorderColor { get; init; }
    public double? BorderWidth { get; init; }
    public double? CornerRadius { get; init; }
}
