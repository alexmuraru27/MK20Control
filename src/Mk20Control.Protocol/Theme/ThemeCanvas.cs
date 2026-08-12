namespace Mk20Control.Protocol.Theme;

/// <summary>The canvas properties of a single theme page ("canvas" object in the layout JSON).</summary>
public sealed record ThemeCanvas
{
    public double? Width { get; init; }
    public double? Height { get; init; }
    public bool? IsFlipped { get; init; }
    public bool? IsRotated { get; init; }
    public bool? ShowUnit { get; init; }
}
