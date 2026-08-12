using Mk20Control.Protocol.Theme.Items;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Fluent builder for a <see cref="BackgroundItem"/> (type 100) - a full-screen static
/// image, GIF, or MP4 video for the main (20-key) or secondary (2.8") screen. Obtained from
/// <see cref="ThemePageBuilder.AddBackground"/>.
/// </summary>
public sealed class BackgroundItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private readonly double _canvasWidth;
    private readonly double _canvasHeight;
    private double _width;
    private double _height;
    private double _x;
    private double _y;
    private BackgroundSurface _surface = BackgroundSurface.Main;
    private string _rawSurface = "main";
    private string? _assetPath;

    internal BackgroundItemBuilder(IThemeAssetRegistry owner, double canvasWidth, double canvasHeight)
    {
        _owner = owner;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        // Confirmed real-theme main-screen background: 640x512 covering the key-grid area,
        // offset down by the status-bar height (y=144), not the full 640x656 canvas.
        _width = canvasWidth;
        _height = canvasHeight - 144;
        _y = 144;
    }

    /// <summary>Sets the background image/GIF asset for the main (20-key) screen.</summary>
    public BackgroundItemBuilder MainScreen(string suggestedFileName, byte[] imageOrGifOrMp4Bytes)
    {
        _surface = BackgroundSurface.Main;
        _rawSurface = "main";
        _assetPath = _owner.RegisterAsset(suggestedFileName, imageOrGifOrMp4Bytes);
        return this;
    }

    /// <summary>Sets the background image/GIF asset for the secondary (2.8") screen.</summary>
    public BackgroundItemBuilder SecondaryScreen(string suggestedFileName, byte[] imageOrGifOrMp4Bytes)
    {
        _surface = BackgroundSurface.Secondary;
        _rawSurface = "secondary";
        _assetPath = _owner.RegisterAsset(suggestedFileName, imageOrGifOrMp4Bytes);
        return this;
    }

    /// <summary>Overrides the auto-derived size/position (defaults to the confirmed 640x512 main-screen coverage area at (0, 144)).</summary>
    public BackgroundItemBuilder At(double x, double y, double width, double height)
    {
        _x = x; _y = y; _width = width; _height = height;
        return this;
    }

    internal ThemeItem Build() => new BackgroundItem
    {
        RawTypeCode = "100",
        Id = _owner.AllocateItemId(),
        X = _x,
        Y = _y,
        Z = -2,
        Width = _width,
        Height = _height,
        Rotate = 0,
        Scale = 1,
        IsLocked = true,
        Surface = _surface,
        RawSurface = _rawSurface,
        AssetPath = _assetPath ?? "",
        RawJson = ThemeItemSkeletons.BackgroundItem(_width, _height),
    };
}
