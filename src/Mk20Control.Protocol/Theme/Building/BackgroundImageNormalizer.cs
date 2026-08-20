using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using Mk20Control.Protocol.Compat;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Resizes/crops arbitrary caller-supplied background images or GIFs to fill an exact
/// target size, matching how every real secondary-screen background asset examined was
/// pre-scaled to precisely the item's declared w/h (e.g. every real secondary-screen GIF is
/// exactly 428x142 - the device does not scale backgrounds at render time; see
/// PROTOCOL_WAVESHARE_MK20.md §7.1). Exists so callers of
/// <see cref="DynamicImageItemBuilder.SecondaryScreenBackground"/> never have to hand-roll
/// their own ImageSharp resize/crop code (and never accidentally upload a background whose
/// asset dimensions don't match its declared item rectangle) - the equivalent of
/// <see cref="IconImageNormalizer"/>, but for full-screen backgrounds instead of key icons.
/// </summary>
public static class BackgroundImageNormalizer
{
    /// <summary>
    /// Loads <paramref name="imageOrGifBytes"/> and resizes/crops every frame (preserving
    /// animation, frame delays, and loop count for a GIF source) to exactly
    /// <paramref name="targetWidth"/>x<paramref name="targetHeight"/>, re-encoding as GIF if
    /// the source has more than one frame, PNG otherwise.
    ///
    /// <paramref name="offsetXPercent"/>/<paramref name="offsetYPercent"/> (each in [-1, 1],
    /// default 0 = centered) pan which part of the source is kept visible when the source's
    /// aspect ratio doesn't match the target and cropping has to discard pixels: -1 shifts
    /// the crop window as far left/up as possible, +1 as far right/down as possible. This is
    /// purely a local image-processing convenience (which pixels of the source survive the
    /// crop) - it does not change the resulting item's on-device x/y/w/h rectangle.
    /// </summary>
    public static byte[] ResizeToFill(byte[] imageOrGifBytes, int targetWidth, int targetHeight, double offsetXPercent = 0, double offsetYPercent = 0)
    {
        Guard.NotNull(imageOrGifBytes);
        if (targetWidth <= 0) throw new ArgumentOutOfRangeException(nameof(targetWidth));
        if (targetHeight <= 0) throw new ArgumentOutOfRangeException(nameof(targetHeight));
        if (offsetXPercent is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(offsetXPercent), "Must be in [-1, 1].");
        if (offsetYPercent is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(offsetYPercent), "Must be in [-1, 1].");

        using var source = Image.Load<Rgba32>(imageOrGifBytes);
        float centerX = (float)(0.5 + offsetXPercent / 2);
        float centerY = (float)(0.5 + offsetYPercent / 2);
        source.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(targetWidth, targetHeight),
            Mode = ResizeMode.Crop,
            CenterCoordinates = new PointF(centerX, centerY),
        }));

        using var ms = new MemoryStream();
        if (source.Frames.Count > 1)
            source.Save(ms, new GifEncoder());
        else
            source.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}
