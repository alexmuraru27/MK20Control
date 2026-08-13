using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Normalizes arbitrary caller-supplied icon images to match the exact PNG format real
/// ScreenKeyWindows-saved themes use for key icons, before they are embedded as theme
/// assets.
///
/// CONFIRMED ROOT CAUSE (found only after a real key built by this library - correct at the
/// JSON/controlData level - still made ScreenKeyWindows itself lock up on load): every real
/// key icon PNG examined from a shipped vendor theme (e.g. `customTheme5buttons.Theme`) is
/// exactly 128x128 pixels, RGB (no alpha channel) - but this project's bundled icon assets
/// (`assets/icons/icon_*.png`) are all 64x64 RGBA. A `KeyItem`'s `scaledWidthTo`/
/// `scaledHeightTo` fields (128 by default, matching every real theme) describe the
/// *rendered* size, but the actual embedded PNG asset itself was still undersized and
/// carried an unexpected alpha channel - this size/format mismatch is confirmed to be able
/// to make the vendor's own image-loading code lock up, independent of any JSON field
/// correctness. This class exists so callers of the builder API never have to pre-process
/// their own icon images to avoid this - every icon passed through
/// <see cref="KeyItemBuilder.Icon"/>/<see cref="ThemeEditor.PageEditor.SetKeyIcon"/> is
/// normalized automatically.
/// </summary>
internal static class IconImageNormalizer
{
    private const int RequiredSize = 128;

    /// <summary>
    /// Re-encodes <paramref name="pngOrOtherImageBytes"/> as a 128x128, RGB (no alpha),
    /// 24-bit PNG - matching every real theme's key icon format exactly. Animated/multi-frame
    /// sources use only the first frame (key icons are always static; use
    /// <c>paths</c>/<c>frameDelays</c> for animated icons instead, see PROTOCOL_WAVESHARE_MK20.md §7.1).
    /// If the input is already exactly 128x128 24-bit RGB PNG, it is still re-encoded (cheap,
    /// and guarantees a canonical/known-good output rather than trusting the caller's claim).
    /// </summary>
    public static byte[] NormalizeToKeyIcon(byte[] pngOrOtherImageBytes)
    {
        ArgumentNullException.ThrowIfNull(pngOrOtherImageBytes);
        using var image = Image.Load<Rgb24>(pngOrOtherImageBytes);

        // Composite onto an opaque black background first (in case the source has
        // transparency) then resize to the confirmed real-hardware icon size.
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(RequiredSize, RequiredSize),
            Mode = ResizeMode.Pad,
            PadColor = Color.Black,
        }));

        return EncodeFrame(image);
    }

    /// <summary>
    /// Decodes an animated image (typically a GIF), splits it into individual frames, and
    /// registers each frame as a separate 128x128 RGB PNG asset under a folder path -
    /// matching the confirmed real mechanism used by an animated KEY item (as opposed to a
    /// type-114 DynamicImageItem's single embedded GIF): folder
    /// "/image/MK20/cache/&lt;suggestedFolderName&gt;/frame_N.png", plus a comma-separated
    /// "frameDelays" string of each frame's display duration in milliseconds - confirmed via
    /// a real user-created theme (customTheme5buttons.Theme's pop-cat key: folder
    /// "pop-cat_1", frames "frame_0.png"/"frame_1.png", frameDelays "100,100").
    /// </summary>
    /// <returns>The folder's virtual path (for <c>KeyItem.paths</c>) and the frameDelays CSV string.</returns>
    public static (string FolderPath, string FrameDelaysCsv) RegisterAnimatedIcon(
        IThemeAssetRegistry registry, string suggestedFolderName, byte[] animatedImageBytes)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(animatedImageBytes);

        using var image = Image.Load<Rgba32>(animatedImageBytes);
        string folderName = System.IO.Path.GetFileNameWithoutExtension(suggestedFolderName);
        string folderPath = $"/image/MK20/cache/{folderName}";

        var delays = new List<int>();
        int frameIndex = 0;
        foreach (var frame in image.Frames)
        {
            using var frameImage = image.Frames.CloneFrame(frameIndex);
            frameImage.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(RequiredSize, RequiredSize),
                Mode = ResizeMode.Pad,
                PadColor = Color.Black,
            }));
            byte[] frameBytes = EncodeFrame(ConvertToRgb24(frameImage));

            // Real assets are registered as "<folderPath>/frame_N.png" - RegisterAsset only
            // takes a suggested file name, so build the full relative path directly and
            // register it via the same underlying asset dictionary the registry exposes.
            registry.RegisterAsset($"{folderName}/frame_{frameIndex}.png", frameBytes);

            var gifMeta = frame.Metadata.GetGifMetadata();
            int delayMs = gifMeta.FrameDelay > 0 ? gifMeta.FrameDelay * 10 : 100; // GIF delay unit is 1/100s
            delays.Add(delayMs);
            frameIndex++;
        }

        return (folderPath, string.Join(",", delays));
    }

    private static Image<Rgb24> ConvertToRgb24(Image<Rgba32> source)
    {
        var result = new Image<Rgb24>(source.Width, source.Height);
        result.Mutate(ctx => ctx.DrawImage(source, 1f));
        return result;
    }

    private static byte[] EncodeFrame(Image<Rgb24> image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder
        {
            ColorType = PngColorType.Rgb,
            BitDepth = PngBitDepth.Bit8,
        });
        return ms.ToArray();
    }
}
