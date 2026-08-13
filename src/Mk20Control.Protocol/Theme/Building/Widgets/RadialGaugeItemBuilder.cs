using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;

using Mk20Control.Protocol.Theme.Building;
namespace Mk20Control.Protocol.Theme.Building.Widgets;

/// <summary>Fluent builder for a <see cref="RadialGaugeItem"/> (type 109) - a data-bound arc gauge with up to 3 gradient stops. Obtained from <see cref="ThemePageBuilder.AddRadialGauge"/>.</summary>
public sealed class RadialGaugeItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private double _x, _y, _z = 1, _scale = 0.5;
    private string? _systemDataName;
    private double _min, _max = 100;
    private double _angleMin = 225, _angleMax = 315;
    private double _arcRadius = 16, _arcInterval = 9, _radius = 100;
    private string? _color1, _color2, _color3;
    private bool _clockwise = true;

    internal RadialGaugeItemBuilder(IThemeAssetRegistry owner) => _owner = owner;

    public RadialGaugeItemBuilder At(double x, double y, double z = 1, double scale = 0.5) { _x = x; _y = y; _z = z; _scale = scale; return this; }

    /// <summary>Binds this gauge's fill level to a live data source (e.g. "CPU Usage", "GPU Usage", "RAM Usage") within [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public RadialGaugeItemBuilder BoundTo(string systemDataName, double min = 0, double max = 100) { _systemDataName = systemDataName; _min = min; _max = max; return this; }

    /// <summary>Sets the arc's angular range in degrees (defaults to 225-315, the confirmed real-theme convention for a bottom-open dial).</summary>
    public RadialGaugeItemBuilder AngleRange(double minDegrees, double maxDegrees) { _angleMin = minDegrees; _angleMax = maxDegrees; return this; }

    public RadialGaugeItemBuilder Arc(double arcRadius, double arcCircularInterval, double radius = 100) { _arcRadius = arcRadius; _arcInterval = arcCircularInterval; _radius = radius; return this; }

    /// <summary>Sets up to 3 gradient stop colors, each as "r=..,g=..,b=..,a=..".</summary>
    public RadialGaugeItemBuilder Gradient(string color1, string? color2 = null, string? color3 = null) { _color1 = color1; _color2 = color2; _color3 = color3; return this; }

    /// <summary>Sets the arc's fill direction (defaults to clockwise=true, the confirmed real-theme default). Confirmed field via widgetThemeDemo.Theme.</summary>
    public RadialGaugeItemBuilder Direction(bool clockwise) { _clockwise = clockwise; return this; }

    internal ThemeItem Build() => new RadialGaugeItem
    {
        RawTypeCode = "109",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Rotate = 0, Scale = _scale, IsLocked = true,
        SystemDataName = _systemDataName,
        MinValue = _min,
        MaxValue = _max,
        AngleMinValue = _angleMin,
        AngleMaxValue = _angleMax,
        ArcRadius = _arcRadius,
        ArcCircularInterval = _arcInterval,
        GradientColor1 = _color1,
        GradientColor2 = _color2,
        GradientColor3 = _color3,
        Clockwise = _clockwise,
        RawJson = ThemeItemSkeletons.RadialGaugeItem(_radius),
    };
}
