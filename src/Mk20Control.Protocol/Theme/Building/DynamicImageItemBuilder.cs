using Mk20Control.Protocol.Theme.Items;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>Fluent builder for a <see cref="DynamicImageItem"/> (type 114) - an animated GIF, optionally data-bound. Obtained from <see cref="ThemePageBuilder.AddDynamicImage"/>.</summary>
public sealed class DynamicImageItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private readonly double _canvasWidth, _canvasHeight;
    private double _x, _y, _z = 1, _w = 428, _h = 142;
    private string? _assetPath;
    private string? _systemDataName;

    internal DynamicImageItemBuilder(IThemeAssetRegistry owner, double canvasWidth, double canvasHeight)
    {
        _owner = owner;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
    }

    public DynamicImageItemBuilder At(double x, double y, double width = 428, double height = 142, double z = 1) { _x = x; _y = y; _w = width; _h = height; _z = z; return this; }

    /// <summary>Sets the animated GIF asset by registering <paramref name="gifBytes"/> as a new theme asset.</summary>
    public DynamicImageItemBuilder Gif(string suggestedFileName, byte[] gifBytes)
    {
        _assetPath = _owner.RegisterAsset(suggestedFileName, gifBytes);
        return this;
    }

    /// <summary>Optionally binds this image's visibility/selection to a live data source.</summary>
    public DynamicImageItemBuilder BoundTo(string systemDataName) { _systemDataName = systemDataName; return this; }

    internal ThemeItem Build() => new DynamicImageItem
    {
        RawTypeCode = "114",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Width = _w, Height = _h, Rotate = 0, Scale = 1, IsLocked = true,
        AssetPath = _assetPath ?? "",
        SystemDataName = _systemDataName,
        RawJson = ThemeItemSkeletons.DynamicImageItem(_w, _h),
    };
}
