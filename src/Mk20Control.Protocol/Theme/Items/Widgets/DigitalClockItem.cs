namespace Mk20Control.Protocol.Theme.Items.Widgets;

/// <summary>
/// A live digital clock display item ("type": "111" in the theme layout JSON). Confirmed
/// present in <c>defaultTheme.Theme</c> as three adjacent items with
/// <c>system_data_name</c> "hour"/"minute"/"second" respectively (each rendering one
/// clock-field digit group) - i.e. a full clock is composed of multiple type-111 items
/// side by side, not one item with an internal hour:minute:second format string.
///
/// <para>
/// <b>Digit rendering (<c>displayType</c>).</b> Two values were found across the shipped
/// vendor themes: <c>"0"</c> draws the digits with the font in <c>text_font</c>, while
/// <c>"1"</c> draws them from a PICTURE FONT - a folder of per-glyph images named by
/// <c>paths</c> (e.g. <c>/image/MK10/PictureFont/点数字</c>). Both are digital faces;
/// <c>displayType</c> is NOT an analog/digital switch. This library always emits
/// <c>displayType: "0"</c> and does not expose the picture-font variant.
/// </para>
///
/// <para>
/// The vendor editor also offers an ANALOG clock face, which is not implemented here: no
/// shipped vendor theme contains one, and no corresponding item type has ever been observed
/// on the wire - see PROTOCOL_WAVESHARE_MK20.md §10 item 15.
/// </para>
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
