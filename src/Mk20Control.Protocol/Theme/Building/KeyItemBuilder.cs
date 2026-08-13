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
    private string? _animatedFolderPath;
    private string? _animatedFrameDelays;
    private Actions.KeyAction? _action;
    private string _title = "";
    private int _opacity = 100;
    private string? _titleFontFamily;
    private double? _titleFontSize;
    private string? _titleAlignment;
    private string? _titleColor;
    // Real key items always have "lock":"1"; default to locked to match.
    private bool _locked = true;

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

    /// <summary>Sets this key's icon by registering <paramref name="pngOrGifBytes"/> as a new theme asset. The image is automatically normalized to the confirmed real-hardware key icon format (128x128, RGB/no-alpha PNG) - callers do not need to pre-resize or flatten their own images.</summary>
    public KeyItemBuilder Icon(string suggestedFileName, byte[] pngOrGifBytes)
    {
        _iconAssetPath = _owner.RegisterAsset(suggestedFileName, IconImageNormalizer.NormalizeToKeyIcon(pngOrGifBytes));
        return this;
    }

    /// <summary>
    /// Sets this key to show a multi-frame animation (e.g. from an animated GIF) instead of
    /// a static icon - this makes the KEY ITSELF animated (still fully pressable/assignable
    /// an action via <see cref="Action"/>), unlike <see cref="ThemePageBuilder.AddDynamicImage"/>
    /// (type 114), which is a separate, non-interactive decorative image with no key
    /// behavior. Confirmed real mechanism: each frame is registered as a separate PNG asset
    /// under a folder path (e.g. "/image/MK20/cache/&lt;name&gt;/frame_0.png",
    /// "frame_1.png", ...), with "paths" set to that folder and "frameDelays" set to a
    /// comma-separated per-frame delay list in milliseconds - "path" is left empty (see
    /// PROTOCOL_WAVESHARE_MK20.md §7.1). <paramref name="gifBytes"/> is decoded and its
    /// frames re-encoded via <see cref="IconImageNormalizer"/> to the confirmed real icon
    /// format (128x128, RGB PNG) automatically.
    /// </summary>
    public KeyItemBuilder AnimatedIcon(string suggestedFolderName, byte[] gifBytes)
    {
        var (folderPath, frameDelaysCsv) = IconImageNormalizer.RegisterAnimatedIcon(_owner, suggestedFolderName, gifBytes);
        _iconAssetPath = null;
        _animatedFolderPath = folderPath;
        _animatedFrameDelays = frameDelaysCsv;
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

    /// <summary>
    /// Sets this key's opacity/transparency, from 0 (fully transparent) to 100 (fully
    /// opaque, the default matching most real theme keys). Confirmed via a real
    /// ScreenKeyWindows capture (tools/Captures/capture19_text_over_buttons_and_txtinput.pcapng
    /// - a key edited in the vendor editor to show a title with a translucent icon):
    /// setting the icon's transparency in the editor's UI produces <c>"opacity":"15"</c> in
    /// the resulting <c>.Theme</c> file - i.e. the same key item, not a separate overlay
    /// item, simply gets its own <c>opacity</c> field lowered alongside its <c>title</c>.
    /// </summary>
    public KeyItemBuilder Opacity(int opacityPercent)
    {
        _opacity = opacityPercent;
        return this;
    }

    /// <summary>
    /// Customizes the on-screen title's font/appearance (the <c>titleParam</c> JSON field).
    /// Any parameter left null keeps the confirmed real default for that sub-field
    /// (Microsoft YaHei, size 24, white, bottom-aligned, title and icon both shown).
    /// <paramref name="alignment"/>: only <c>"top"</c> and <c>"bottom"</c> are confirmed
    /// real values (observed across every vendor theme examined) - no other value (e.g.
    /// "center") was found in any real theme, and passing one produced no visible centering
    /// effect on real hardware (falls back to the default rendering, likely "bottom").
    /// </summary>
    public KeyItemBuilder TitleStyle(string? fontFamily = null, double? fontSize = null, string? alignment = null, string? colorHex = null)
    {
        _titleFontFamily = fontFamily;
        _titleFontSize = fontSize;
        _titleAlignment = alignment;
        _titleColor = colorHex;
        return this;
    }

    /// <summary>Marks the key as locked/unlocked. Real key items always have "lock":"1"; defaults to locked to match.</summary>
    public KeyItemBuilder Locked(bool locked = true)
    {
        _locked = locked;
        return this;
    }

    internal ThemeItem Build()
    {
        string id = _owner.AllocateItemId();
        return new KeyItem
        {
            RawTypeCode = "115",
            Id = id,
            // Confirmed via --dump-raw-json against real hardware themes: every real KeyItem
            // has an "itemName" (e.g. "control1", "control2", ...) - previously omitted here,
            // leaving newly-built/added keys without this field entirely (SetOrRemove strips
            // a null value's key rather than emitting it empty).
            ItemName = $"control{id}",
            X = _x,
            Y = _y,
            Z = _z,
            Rotate = 0,
            Scale = 1,
            IsLocked = _locked,
            Row = _row,
            Column = _column,
            // For an animated key, "path" must stay "" (see ThemeItemSkeletons.KeyItem) and
            // only "paths" (the folder) is set - IconAssetPath must NOT be populated here,
            // since ThemeFileCodec.BuildItemJson uses it to overwrite "path" directly.
            IconAssetPath = _iconAssetPath,
            Action = _action,
            RawJson = ThemeItemSkeletons.KeyItem(
                _maxWidth, _maxHeight, _scaledWidthTo, _scaledHeightTo, _title,
                titleParam: BuildTitleParamOverride(),
                opacity: _opacity.ToString(),
                paths: _animatedFolderPath ?? "",
                frameDelays: _animatedFrameDelays),
        };
    }

    /// <summary>Builds a custom titleParam JSON string if any style override was set via <see cref="TitleStyle"/>, or null to keep the confirmed real default.</summary>
    private string? BuildTitleParamOverride()
    {
        if (_titleFontFamily is null && _titleFontSize is null && _titleAlignment is null && _titleColor is null)
            return null;

        var obj = new System.Text.Json.Nodes.JsonObject
        {
            ["FontFamily"] = _titleFontFamily ?? "Microsoft YaHei",
            ["FontSize"] = _titleFontSize ?? 24,
            ["FontStyle"] = "",
            ["FontUnderline"] = false,
            ["ShowImage"] = true,
            ["ShowTitle"] = true,
            ["TitleAlignment"] = _titleAlignment ?? "bottom",
            ["TitleColor"] = _titleColor ?? "#ffffff",
        };
        return obj.ToJsonString();
    }
}
