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
    private string? _backgroundType;

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

    /// <summary>
    /// Marks this item as the MAIN (20-key) screen's static picture background, embedded as
    /// a type-114 DynamicImageItem rather than a type-100 BackgroundItem - confirmed via a
    /// genuine ScreenKeyWindows-saved reference (the user added a picture as the main-screen
    /// background through the vendor editor and saved; capturing/decoding that upload showed
    /// a DynamicImageItem at x=0,y=144,w=640,h=512,z=-2 with <c>"backgroundType":"main"</c>
    /// and asset path <c>/image/640x656/cache/&lt;file&gt;</c> - NOT the
    /// <c>/theme/MK20-PLUS/MainScreen/&lt;file&gt;</c> namespace real .mp4-based
    /// BackgroundItems use, and NOT a type-100 item at all). The original file extension is
    /// preserved by the vendor editor (e.g. ".jpg" stays ".jpg", not converted to PNG) -
    /// this method does the same, registering <paramref name="imageBytes"/> byte-for-byte.
    ///
    /// <para>
    /// GIFs ARE also confirmed to work here via a second genuine reference capture (the user
    /// added an animated GIF as the main-screen background and it rendered correctly) - the
    /// item's JSON field set is IDENTICAL between the static-image and GIF cases (only the
    /// asset's file extension/content differs). The one confirmed difference: the vendor
    /// editor did NOT resize the GIF to fill 640x512 (it embedded the original 128x128
    /// source unchanged), unlike a static image (which WAS resized/cropped to exactly
    /// 640x512) - callers wanting a GIF background here should likely pass the image at
    /// whatever size they want rendered, not necessarily 640x512; this has not been
    /// exhaustively tested for every size/positioning behavior.
    /// </para>
    /// See PROTOCOL_WAVESHARE_MK20.md §6.5/§10 Item #14 for the full investigation.
    /// </summary>
    public DynamicImageItemBuilder MainScreenBackground(string suggestedFileName, byte[] imageBytes)
    {
        _x = 0; _y = 144; _w = 640; _h = 512; _z = -2;
        _backgroundType = "main";
        _assetPath = _owner.RegisterAssetAtPath($"/image/640x656/cache/{suggestedFileName}", imageBytes);
        return this;
    }

    /// <summary>
    /// Marks this item as the secondary (2.8") screen's background image/GIF, embedded
    /// directly inside this (640x656) main-screen page - confirmed via a real theme
    /// (defaultTheme.Theme): a DynamicImageItem at the fixed position/size (x=106, y=0,
    /// w=428, h=142) with <c>"backgroundType":"secondary"</c>. Also auto-registers
    /// <paramref name="gifOrImageBytes"/> as the asset, equivalent to calling
    /// <see cref="Gif"/> at that position/size. Use a plain (non-animated) PNG/JPEG here if
    /// you don't want an animation - only the file bytes matter, not the extension.
    ///
    /// <paramref name="gifOrImageBytes"/> is registered byte-for-byte as-is (no implicit
    /// resizing) - callers are expected to pre-size it to exactly 428x142 themselves (every
    /// real secondary-screen background asset examined was pre-scaled to this exact size;
    /// the device does not scale it at render time). Use
    /// <see cref="SecondaryScreenBackgroundAutoFit"/> instead if you want this library to
    /// resize/crop (and optionally pan) an arbitrary source image for you.
    /// </summary>
    public DynamicImageItemBuilder SecondaryScreenBackground(string suggestedFileName, byte[] gifOrImageBytes)
    {
        _x = 106; _y = 0; _w = 428; _h = 142; _z = 1;
        _backgroundType = "secondary";
        // Confirmed real path convention for the secondary-screen background asset,
        // observed in defaultTheme.Theme's own embedded secondary-screen image
        // ("/image/428x142/PhotoAlbum/<file>") - a different namespace than key icons.
        _assetPath = _owner.RegisterAssetAtPath($"/image/428x142/PhotoAlbum/{suggestedFileName}", gifOrImageBytes);
        return this;
    }

    /// <summary>
    /// Same as <see cref="SecondaryScreenBackground"/>, but first resizes/crops
    /// <paramref name="imageOrGifBytes"/> to exactly fill the confirmed real 428x142
    /// secondary-screen area via <see cref="BackgroundImageNormalizer.ResizeToFill"/> - a
    /// size guard/auto-resize for callers who don't want to pre-process their own source
    /// image. <paramref name="offsetXPercent"/>/<paramref name="offsetYPercent"/> (each in
    /// [-1, 1], default 0 = centered) pan which part of the source survives the crop when
    /// its aspect ratio doesn't match 428x142 - see <see cref="BackgroundImageNormalizer.ResizeToFill"/>.
    /// </summary>
    public DynamicImageItemBuilder SecondaryScreenBackgroundAutoFit(string suggestedFileName, byte[] imageOrGifBytes, double offsetXPercent = 0, double offsetYPercent = 0)
        => SecondaryScreenBackground(suggestedFileName, BackgroundImageNormalizer.ResizeToFill(imageOrGifBytes, 428, 142, offsetXPercent, offsetYPercent));

    /// <summary>
    /// Same as <see cref="MainScreenBackground"/>, but first resizes/crops
    /// <paramref name="imageOrGifBytes"/> to exactly fill the confirmed real 640x512
    /// main-screen area via <see cref="BackgroundImageNormalizer.ResizeToFill"/>.
    /// <paramref name="offsetXPercent"/>/<paramref name="offsetYPercent"/> (each in [-1, 1],
    /// default 0 = centered) pan which part of the source survives the crop.
    /// </summary>
    public DynamicImageItemBuilder MainScreenBackgroundAutoFit(string suggestedFileName, byte[] imageOrGifBytes, double offsetXPercent = 0, double offsetYPercent = 0)
        => MainScreenBackground(suggestedFileName, BackgroundImageNormalizer.ResizeToFill(imageOrGifBytes, 640, 512, offsetXPercent, offsetYPercent));

    internal ThemeItem Build() => new DynamicImageItem
    {
        RawTypeCode = "114",
        Id = _owner.AllocateItemId(),
        X = _x, Y = _y, Z = _z, Width = _w, Height = _h, Rotate = 0, Scale = 1, IsLocked = true,
        AssetPath = _assetPath ?? "",
        SystemDataName = _systemDataName,
        BackgroundType = _backgroundType,
        RawJson = ThemeItemSkeletons.DynamicImageItem(_w, _h, _backgroundType),
    };
}
