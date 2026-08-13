using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;

using Mk20Control.Protocol.Theme.Building;
namespace Mk20Control.Protocol.Theme.Building.Widgets;

/// <summary>
/// Fluent builder for a <see cref="CircularGaugeItem"/> (type 101, plain) or <see
/// cref="SegmentedCircularGaugeItem"/> (type 104, "seg-circular" - identical JSON field set,
/// only the type code differs). Obtained from <see cref="ThemePageBuilder.AddCircularGauge"/>
/// / <see cref="ThemePageBuilder.AddSegmentedCircularGauge"/>.
/// </summary>
public sealed class CircularGaugeItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private readonly bool _segmented;
    private double _x, _y, _z = 1;
    private string? _systemDataName;
    private double _min, _max = 100;
    private string _frontColor = "r=0,g=170,b=255,a=255";
    private string _backColor = "r=255,g=255,b=255,a=160";
    private double _margin = 20;
    private double _radius = 100;

    internal CircularGaugeItemBuilder(IThemeAssetRegistry owner, bool segmented) { _owner = owner; _segmented = segmented; }

    public CircularGaugeItemBuilder At(double x, double y, double z = 1) { _x = x; _y = y; _z = z; return this; }

    public CircularGaugeItemBuilder BoundTo(string systemDataName, double min = 0, double max = 100) { _systemDataName = systemDataName; _min = min; _max = max; return this; }

    public CircularGaugeItemBuilder Colors(string frontRgba, string backRgba) { _frontColor = frontRgba; _backColor = backRgba; return this; }

    public CircularGaugeItemBuilder Geometry(double margin, double radius) { _margin = margin; _radius = radius; return this; }

    internal ThemeItem Build()
    {
        var rawJson = ThemeItemSkeletons.CircularGaugeItem(_frontColor, _backColor, _margin, _radius);
        string id = _owner.AllocateItemId();
        return _segmented
            ? new SegmentedCircularGaugeItem
            {
                RawTypeCode = "104", Id = id, X = _x, Y = _y, Z = _z, Rotate = 0, Scale = 1, IsLocked = true,
                SystemDataName = _systemDataName, MinValue = _min, MaxValue = _max,
                FrontColor = _frontColor, BackColor = _backColor, Margin = _margin, Radius = _radius,
                RawJson = rawJson,
            }
            : new CircularGaugeItem
            {
                RawTypeCode = "101", Id = id, X = _x, Y = _y, Z = _z, Rotate = 0, Scale = 1, IsLocked = true,
                SystemDataName = _systemDataName, MinValue = _min, MaxValue = _max,
                FrontColor = _frontColor, BackColor = _backColor, Margin = _margin, Radius = _radius,
                RawJson = rawJson,
            };
    }
}
