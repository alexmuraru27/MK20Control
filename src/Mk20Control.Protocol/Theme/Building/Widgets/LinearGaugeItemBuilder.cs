using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;

using Mk20Control.Protocol.Theme.Building;
namespace Mk20Control.Protocol.Theme.Building.Widgets;

/// <summary>Fluent builder for a <see cref="LinearGaugeItem"/> (type 103) - a data-bound bar with solid front/back/border colors (no gradient). Obtained from <see cref="ThemePageBuilder.AddLinearGauge"/>.</summary>
public sealed class LinearGaugeItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private double _x, _y, _z = 1, _w = 52, _h = 9;
    private string? _systemDataName;
    private double _min, _max = 100;
    private ThemeColor _frontColor = ThemeColor.Parse("r=0,g=170,b=255,a=255");
    private ThemeColor _backColor = ThemeColor.Parse("r=255,g=255,b=255,a=160");
    private ThemeColor _borderColor = ThemeColor.Parse("r=0,g=0,b=255,a=0");
    private double _borderWidth = 2;

    internal LinearGaugeItemBuilder(IThemeAssetRegistry owner) => _owner = owner;

    public LinearGaugeItemBuilder At(double x, double y, double width, double height, double z = 1) { _x = x; _y = y; _w = width; _h = height; _z = z; return this; }

    public LinearGaugeItemBuilder BoundTo(string systemDataName, double min = 0, double max = 100) { _systemDataName = systemDataName; _min = min; _max = max; return this; }

    public LinearGaugeItemBuilder Colors(ThemeColor frontRgba, ThemeColor backRgba, ThemeColor borderRgba, double borderWidth = 2)
    {
        _frontColor = frontRgba; _backColor = backRgba; _borderColor = borderRgba; _borderWidth = borderWidth;
        return this;
    }

    internal ThemeItem Build() => new LinearGaugeItem
    {
        RawTypeCode = "103",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Width = _w, Height = _h, Rotate = 0, Scale = 1, IsLocked = true,
        SystemDataName = _systemDataName,
        MinValue = _min,
        MaxValue = _max,
        FrontColor = _frontColor.ToWireString(),
        BackColor = _backColor.ToWireString(),
        BorderColor = _borderColor.ToWireString(),
        BorderWidth = _borderWidth,
        RawJson = ThemeItemSkeletons.LinearGaugeItem(_frontColor.ToWireString(), _backColor.ToWireString(), _borderColor.ToWireString(), _borderWidth),
    };
}
