using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;

using Mk20Control.Protocol.Theme.Building;
namespace Mk20Control.Protocol.Theme.Building.Widgets;

/// <summary>Fluent builder for a <see cref="ProgressBarItem"/> (type 102) - a data-bound circular/linear bar. Obtained from <see cref="ThemePageBuilder.AddProgressBar"/>.</summary>
public sealed class ProgressBarItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private double _x, _y, _z = 1, _w = 80, _h = 12;
    private string? _systemDataName;
    private double _min, _max = 100;
    private string _frontColor = "r=0,g=255,b=255,a=255";
    private string _backColor = "r=255,g=255,b=255,a=100";
    private string _borderColor = "r=0,g=0,b=255,a=0";
    private double _borderWidth = 2;
    private double _cornerRadius = 5;

    internal ProgressBarItemBuilder(IThemeAssetRegistry owner) => _owner = owner;

    public ProgressBarItemBuilder At(double x, double y, double width, double height, double z = 1) { _x = x; _y = y; _w = width; _h = height; _z = z; return this; }

    /// <summary>Binds this bar's fill level to a live data source (e.g. "Volume", "device_bl", "CPU Usage") within [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public ProgressBarItemBuilder BoundTo(string systemDataName, double min = 0, double max = 100) { _systemDataName = systemDataName; _min = min; _max = max; return this; }

    public ProgressBarItemBuilder Colors(string frontRgba, string backRgba, string borderRgba, double borderWidth = 2, double cornerRadius = 5)
    {
        _frontColor = frontRgba; _backColor = backRgba; _borderColor = borderRgba; _borderWidth = borderWidth; _cornerRadius = cornerRadius;
        return this;
    }

    internal ThemeItem Build() => new ProgressBarItem
    {
        RawTypeCode = "102",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Width = _w, Height = _h, Rotate = 0, Scale = 1, IsLocked = true,
        SystemDataName = _systemDataName,
        MinValue = _min,
        MaxValue = _max,
        RawJson = ThemeItemSkeletons.ProgressBarItem(_frontColor, _backColor, _borderColor, _borderWidth, _cornerRadius),
    };
}
