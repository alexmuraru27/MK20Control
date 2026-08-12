using Mk20Control.Protocol.Theme.Items;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Fluent builder for a <see cref="KeyItem"/> (type 115) - the primary surface for "set a
/// picture on this button and make it do X". Obtained from <see cref="ThemePageBuilder.AddKey"/>.
/// </summary>
public sealed class KeyItemBuilder
{
    private readonly IThemeAssetRegistry _owner;
    private readonly int _row;
    private readonly int _column;
    private double _x;
    private double _y;
    private double _z = 1;
    private readonly double _maxWidth;
    private readonly double _maxHeight;
    private double _scaledWidthTo = 128;
    private double _scaledHeightTo = 128;
    private string? _iconAssetPath;
    private Actions.KeyAction? _action;
    private string _title = "";
    private bool _locked;

    internal KeyItemBuilder(IThemeAssetRegistry owner, int row, int column, double canvasWidth, double canvasHeight)
    {
        _owner = owner;
        _row = row;
        _column = column;
        _maxWidth = canvasWidth;
        _maxHeight = canvasHeight;
        // Confirmed key-grid layout: 128x128 cells, main-screen grid origin at (0, 144).
        _x = column * 128;
        _y = 144 + row * 128;
    }

    /// <summary>Overrides the auto-derived cell position/z-order (defaults are derived from row/column assuming 128x128 cells at the confirmed grid origin).</summary>
    public KeyItemBuilder At(double x, double y, double z = 1)
    {
        _x = x; _y = y; _z = z;
        return this;
    }

    /// <summary>Overrides the rendered icon size in pixels (defaults to 128x128, matching every real theme observed).</summary>
    public KeyItemBuilder IconSize(double width, double height)
    {
        _scaledWidthTo = width; _scaledHeightTo = height;
        return this;
    }

    /// <summary>Sets this key's icon by registering <paramref name="pngOrGifBytes"/> as a new theme asset.</summary>
    public KeyItemBuilder Icon(string suggestedFileName, byte[] pngOrGifBytes)
    {
        _iconAssetPath = _owner.RegisterAsset(suggestedFileName, pngOrGifBytes);
        return this;
    }

    /// <summary>Sets this key's icon to an already-registered asset path (e.g. shared across multiple keys).</summary>
    public KeyItemBuilder IconAssetPath(string assetPath)
    {
        _iconAssetPath = assetPath;
        return this;
    }

    /// <summary>Assigns this key's behavior - build one via <see cref="KeyActions"/> (e.g. <c>KeyActions.Keyboard(...)</c>).</summary>
    public KeyItemBuilder Action(Actions.KeyAction action)
    {
        _action = action;
        return this;
    }

    /// <summary>Sets the key's on-screen title/label text (shown per <c>titleParam.ShowTitle</c>; empty by default, matching most real theme keys).</summary>
    public KeyItemBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>Marks the key as locked in the editor (position-locked; does not affect device behavior). Real themes typically set this to true/"1".</summary>
    public KeyItemBuilder Locked(bool locked = true)
    {
        _locked = locked;
        return this;
    }

    internal ThemeItem Build() => new KeyItem
    {
        RawTypeCode = "115",
        Id = _owner.AllocateItemId(),
        X = _x,
        Y = _y,
        Z = _z,
        Rotate = 0,
        Scale = 1,
        IsLocked = _locked,
        Row = _row,
        Column = _column,
        IconAssetPath = _iconAssetPath,
        Action = _action,
        RawJson = ThemeItemSkeletons.KeyItem(_maxWidth, _maxHeight, _scaledWidthTo, _scaledHeightTo, _title),
    };
}
