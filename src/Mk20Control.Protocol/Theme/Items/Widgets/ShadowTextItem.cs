namespace Mk20Control.Protocol.Theme.Items.Widgets;

/// <summary>
/// A drop-shadow text item ("type": "117" in the theme layout JSON) - same base field set as
/// <see cref="TextItem"/> (type "113") plus a border stroke (<c>border_color</c>/
/// <c>border_width</c>) and a drop-shadow (<c>shadeColor</c>/<c>shadeSize</c>). Confirmed
/// present in <c>widgetThemeDemo.Theme</c> (itemName "Shadow Text3") bound to
/// "Disk Usage (D:/)".
/// </summary>
public sealed record ShadowTextItem : ThemeItem
{
    public string? SystemDataName { get; init; }
    public string? Text { get; init; }
    public string? Font { get; init; }
    public string? FrontColor { get; init; }

    /// <summary>Text outline/stroke color, "r=...,g=...,b=...,a=..." format.</summary>
    public string? BorderColor { get; init; }
    public double? BorderWidth { get; init; }

    /// <summary>Drop-shadow color, "r=...,g=...,b=...,a=..." format.</summary>
    public string? ShadeColor { get; init; }
    public double? ShadeSize { get; init; }
}
