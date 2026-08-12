using Mk20Control.Protocol.Theme.Items;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>Fluent builder for a <see cref="TextItem"/> (type 113) - static or data-bound text. Obtained from <see cref="ThemePageBuilder.AddText"/>.</summary>
public sealed class TextItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private double _x, _y, _z = 1;
    private string? _systemDataName;
    private string _text = "Text";
    private string _font = "Microsoft YaHei,72,-1,5,50,0,0,0,0,0";
    private string _frontColor = "r=255,g=255,b=255,a=255";
    private double _scale = 1;

    internal TextItemBuilder(IThemeAssetRegistry owner) => _owner = owner;

    /// <summary>Sets the item's position and stacking order.</summary>
    public TextItemBuilder At(double x, double y, double z = 1) { _x = x; _y = y; _z = z; return this; }

    /// <summary>Sets static text content (not data-bound).</summary>
    public TextItemBuilder Text(string text) { _text = text; _systemDataName = null; return this; }

    /// <summary>Binds this text's value to a live data source (e.g. "CPU Usage", "device_bl", "Volume") - see PROTOCOL_WAVESHARE_MK20.md for confirmed source names.</summary>
    public TextItemBuilder BoundTo(string systemDataName) { _systemDataName = systemDataName; return this; }

    /// <summary>Sets the font descriptor string (family,size,-1,5,weight,0,0,0,0,0[,style]) and render scale (real themes commonly use a small scale like 0.2-0.3 with a large point size).</summary>
    public TextItemBuilder Font(string font, double scale = 1) { _font = font; _scale = scale; return this; }

    /// <summary>Sets the text color as an "r=..,g=..,b=..,a=.." string (0-255 each channel).</summary>
    public TextItemBuilder Color(string rgba) { _frontColor = rgba; return this; }

    internal ThemeItem Build() => new TextItem
    {
        RawTypeCode = "113",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Rotate = 0, Scale = _scale, IsLocked = true,
        SystemDataName = _systemDataName,
        Text = _text,
        Font = _font,
        RawJson = ThemeItemSkeletons.TextItem(_frontColor, _font),
    };
}
