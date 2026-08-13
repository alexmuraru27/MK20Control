namespace Mk20Control.Protocol.Theme.Items.Widgets;

/// <summary>
/// A multi-line text item ("type": "116" in the theme layout JSON) - identical field set to
/// <see cref="TextItem"/> (type "113") plus explicit <c>w</c>/<c>h</c> bounds for text
/// wrapping. Confirmed present in <c>widgetThemeDemo.Theme</c> (itemName "Mult-Text2") bound
/// to "Disk Total Space (C:/)".
/// </summary>
public sealed record MultilineTextItem : ThemeItem
{
    public string? SystemDataName { get; init; }
    public string? Text { get; init; }
    public string? Font { get; init; }
    public string? FrontColor { get; init; }
}
