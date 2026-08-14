using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;

using Mk20Control.Protocol.Theme.Building;
namespace Mk20Control.Protocol.Theme.Building.Widgets;

/// <summary>Fluent builder for a <see cref="LightShadowGaugeItem"/> (type 110) - a data-bound ring with an arc stroke plus a glow/shadow highlight. Obtained from <see cref="ThemePageBuilder.AddLightShadowGauge"/>.</summary>
public sealed class LightShadowGaugeItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private double _x, _y, _z = 1;
    private string? _systemDataName;
    private double _min, _max = 100;
    private ThemeColor _backColor = ThemeColor.Parse("r=0,g=255,b=0,a=255");
    private ThemeColor _arcColor = ThemeColor.Parse("r=0,g=0,b=255,a=255");
    private double _arcWidth = 6;
    private double _radius = 50;
    private bool _clockwise = true;
    private double _displayDirection = 1;
    private ThemeColor _lightShadowColor = ThemeColor.Parse("r=255,g=0,b=0,a=255");
    private double _lightShadowLighter = 100;
    private double _lightShadowPosition = 80;

    internal LightShadowGaugeItemBuilder(IThemeAssetRegistry owner) => _owner = owner;

    public LightShadowGaugeItemBuilder At(double x, double y, double z = 1) { _x = x; _y = y; _z = z; return this; }

    public LightShadowGaugeItemBuilder BoundTo(string systemDataName, double min = 0, double max = 100) { _systemDataName = systemDataName; _min = min; _max = max; return this; }

    public LightShadowGaugeItemBuilder Colors(ThemeColor backRgba, ThemeColor arcRgba, double arcWidth = 6) { _backColor = backRgba; _arcColor = arcRgba; _arcWidth = arcWidth; return this; }

    public LightShadowGaugeItemBuilder Geometry(double radius, bool clockwise = true, double displayDirection = 1) { _radius = radius; _clockwise = clockwise; _displayDirection = displayDirection; return this; }

    public LightShadowGaugeItemBuilder LightShadow(ThemeColor colorRgba, double lighter = 100, double position = 80) { _lightShadowColor = colorRgba; _lightShadowLighter = lighter; _lightShadowPosition = position; return this; }

    internal ThemeItem Build() => new LightShadowGaugeItem
    {
        RawTypeCode = "110",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Rotate = 0, Scale = 1, IsLocked = true,
        SystemDataName = _systemDataName,
        MinValue = _min,
        MaxValue = _max,
        BackColor = _backColor.ToWireString(),
        ArcColor = _arcColor.ToWireString(),
        ArcWidth = _arcWidth,
        Radius = _radius,
        Clockwise = _clockwise,
        DisplayDirection = _displayDirection,
        LightShadowColor = _lightShadowColor.ToWireString(),
        LightShadowLighter = _lightShadowLighter,
        LightShadowPosition = _lightShadowPosition,
        RawJson = ThemeItemSkeletons.LightShadowGaugeItem(_backColor.ToWireString(), _arcColor.ToWireString(), _arcWidth, _radius, _clockwise, _displayDirection, _lightShadowColor.ToWireString(), _lightShadowLighter, _lightShadowPosition),
    };
}
