using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;

using Mk20Control.Protocol.Theme.Building;
namespace Mk20Control.Protocol.Theme.Building.Widgets;

/// <summary>Fluent builder for a <see cref="ShadowTextItem"/> (type 117) - static or data-bound text with a border stroke and drop-shadow. Obtained from <see cref="ThemePageBuilder.AddShadowText"/>.</summary>
public sealed class ShadowTextItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private double _x, _y, _z = 1;
    private string? _systemDataName;
    private string? _text;
    private string _font = "Microsoft YaHei,65,-1,5,50,0,0,0,0,0";
    private string _frontColor = "r=255,g=255,b=255,a=255";
    private string _borderColor = "r=23,g=54,b=255,a=255";
    private double _borderWidth = 5;
    private string _shadeColor = "r=0,g=0,b=0,a=128";
    private double _shadeSize = 10;

    internal ShadowTextItemBuilder(IThemeAssetRegistry owner) => _owner = owner;

    public ShadowTextItemBuilder At(double x, double y, double z = 1) { _x = x; _y = y; _z = z; return this; }

    /// <summary>Sets static text content; mutually exclusive with <see cref="BoundTo"/>.</summary>
    public ShadowTextItemBuilder Text(string text) { _text = text; _systemDataName = null; return this; }

    /// <summary>Binds this text's content to a live data source; mutually exclusive with <see cref="Text"/>.</summary>
    public ShadowTextItemBuilder BoundTo(string systemDataName) { _systemDataName = systemDataName; return this; }

    public ShadowTextItemBuilder Font(string fontDescriptor) { _font = fontDescriptor; return this; }

    public ShadowTextItemBuilder Color(string frontRgba) { _frontColor = frontRgba; return this; }

    public ShadowTextItemBuilder Border(string borderRgba, double borderWidth = 5) { _borderColor = borderRgba; _borderWidth = borderWidth; return this; }

    public ShadowTextItemBuilder Shadow(string shadeRgba, double shadeSize = 10) { _shadeColor = shadeRgba; _shadeSize = shadeSize; return this; }

    internal ThemeItem Build() => new ShadowTextItem
    {
        RawTypeCode = "117",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Rotate = 0, Scale = 1, IsLocked = true,
        SystemDataName = _systemDataName,
        Text = _text ?? "Text",
        Font = _font,
        FrontColor = _frontColor,
        BorderColor = _borderColor,
        BorderWidth = _borderWidth,
        ShadeColor = _shadeColor,
        ShadeSize = _shadeSize,
        RawJson = ThemeItemSkeletons.ShadowTextItem(_frontColor, _font, _borderColor, _borderWidth, _shadeColor, _shadeSize),
    };
}
