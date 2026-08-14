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
    private ThemeColor _frontColor = ThemeColor.Parse("r=255,g=255,b=255,a=255");
    private ThemeColor _borderColor = ThemeColor.Parse("r=23,g=54,b=255,a=255");
    private double _borderWidth = 5;
    private ThemeColor _shadeColor = ThemeColor.Parse("r=0,g=0,b=0,a=128");
    private double _shadeSize = 10;

    internal ShadowTextItemBuilder(IThemeAssetRegistry owner) => _owner = owner;

    public ShadowTextItemBuilder At(double x, double y, double z = 1) { _x = x; _y = y; _z = z; return this; }

    /// <summary>Sets static text content; mutually exclusive with <see cref="BoundTo"/>.</summary>
    public ShadowTextItemBuilder Text(string text) { _text = text; _systemDataName = null; return this; }

    /// <summary>Binds this text's content to a live data source; mutually exclusive with <see cref="Text"/>.</summary>
    public ShadowTextItemBuilder BoundTo(string systemDataName) { _systemDataName = systemDataName; return this; }

    public ShadowTextItemBuilder Font(string fontDescriptor) { _font = fontDescriptor; return this; }

    public ShadowTextItemBuilder Color(ThemeColor frontRgba) { _frontColor = frontRgba; return this; }

    public ShadowTextItemBuilder Border(ThemeColor borderRgba, double borderWidth = 5) { _borderColor = borderRgba; _borderWidth = borderWidth; return this; }

    public ShadowTextItemBuilder Shadow(ThemeColor shadeRgba, double shadeSize = 10) { _shadeColor = shadeRgba; _shadeSize = shadeSize; return this; }

    internal ThemeItem Build() => new ShadowTextItem
    {
        RawTypeCode = "117",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Rotate = 0, Scale = 1, IsLocked = true,
        SystemDataName = _systemDataName,
        Text = _text ?? "Text",
        Font = _font,
        FrontColor = _frontColor.ToWireString(),
        BorderColor = _borderColor.ToWireString(),
        BorderWidth = _borderWidth,
        ShadeColor = _shadeColor.ToWireString(),
        ShadeSize = _shadeSize,
        RawJson = ThemeItemSkeletons.ShadowTextItem(_frontColor.ToWireString(), _font, _borderColor.ToWireString(), _borderWidth, _shadeColor.ToWireString(), _shadeSize),
    };
}
