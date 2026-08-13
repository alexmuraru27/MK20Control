using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;

using Mk20Control.Protocol.Theme.Building;
namespace Mk20Control.Protocol.Theme.Building.Widgets;

/// <summary>
/// Fluent builder for one <see cref="DigitalClockItem"/> field (type 111) - "hour",
/// "minute", or "second". A full clock display is composed of 2-3 adjacent items (one per
/// field), matching the pattern observed in defaultTheme.Theme. Obtained from
/// <see cref="ThemePageBuilder.AddDigitalClockField"/>.
/// </summary>
public sealed class DigitalClockItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private double _x, _y, _z = 1, _w = 128, _h = 128;
    private string _field = "minute";
    private string _font = "Microsoft YaHei,12,-1,5,50,0,0,0,0,0";
    private string _frontColor = "r=255,g=255,b=255,a=255";
    private string _backColor = "r=245,g=245,b=245,a=0";
    private string _borderColor = "r=000,g=000,b=255,a=255";
    private int _displayNum = 2;

    internal DigitalClockItemBuilder(IThemeAssetRegistry owner) => _owner = owner;

    public DigitalClockItemBuilder At(double x, double y, double width = 128, double height = 128, double z = 1) { _x = x; _y = y; _w = width; _h = height; _z = z; return this; }

    /// <summary>Which clock field this item renders - confirmed values: "hour", "minute", "second".</summary>
    public DigitalClockItemBuilder Field(string systemDataName, int displayDigits = 2) { _field = systemDataName; _displayNum = displayDigits; return this; }

    public DigitalClockItemBuilder Font(string font) { _font = font; return this; }

    public DigitalClockItemBuilder Colors(string frontRgba, string backRgba, string borderRgba) { _frontColor = frontRgba; _backColor = backRgba; _borderColor = borderRgba; return this; }

    internal ThemeItem Build() => new DigitalClockItem
    {
        RawTypeCode = "111",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Width = _w, Height = _h, Rotate = 0, Scale = 1, IsLocked = true,
        SystemDataName = _field,
        Font = _font,
        FrontColor = _frontColor,
        BackColor = _backColor,
        BorderColor = _borderColor,
        BorderWidth = 0,
        CornerRadius = 0,
        RawJson = ThemeItemSkeletons.DigitalClockItem(_frontColor, _backColor, _borderColor, _font, _displayNum),
    };
}
