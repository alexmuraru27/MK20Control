using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Items.Widgets;

using Mk20Control.Protocol.Theme.Building;
namespace Mk20Control.Protocol.Theme.Building.Widgets;

/// <summary>Fluent builder for a <see cref="MultilineTextItem"/> (type 116) - a static or data-bound wrapping text block. Obtained from <see cref="ThemePageBuilder.AddMultilineText"/>.</summary>
public sealed class MultilineTextItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private double _x, _y, _z = 1, _w = 200, _h = 100;
    private string? _systemDataName;
    private string? _text;
    private string _font = "Microsoft YaHei,20,-1,5,50,0,0,0,0,0";
    private ThemeColor _frontColor = ThemeColor.Parse("r=255,g=255,b=255,a=255");

    internal MultilineTextItemBuilder(IThemeAssetRegistry owner) => _owner = owner;

    public MultilineTextItemBuilder At(double x, double y, double width = 200, double height = 100, double z = 1) { _x = x; _y = y; _w = width; _h = height; _z = z; return this; }

    /// <summary>Sets static text content; mutually exclusive with <see cref="BoundTo"/>.</summary>
    public MultilineTextItemBuilder Text(string text) { _text = text; _systemDataName = null; return this; }

    /// <summary>Binds this text's content to a live data source; mutually exclusive with <see cref="Text"/>.</summary>
    public MultilineTextItemBuilder BoundTo(string systemDataName) { _systemDataName = systemDataName; return this; }

    public MultilineTextItemBuilder Font(string fontDescriptor) { _font = fontDescriptor; return this; }

    public MultilineTextItemBuilder Color(ThemeColor frontRgba) { _frontColor = frontRgba; return this; }

    internal ThemeItem Build() => new MultilineTextItem
    {
        RawTypeCode = "116",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Width = _w, Height = _h, Rotate = 0, Scale = 1, IsLocked = true,
        SystemDataName = _systemDataName,
        Text = _text ?? "Text",
        Font = _font,
        FrontColor = _frontColor.ToWireString(),
        RawJson = ThemeItemSkeletons.MultilineTextItem(_frontColor.ToWireString(), _font),
    };
}
