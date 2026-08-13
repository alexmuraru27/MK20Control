using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Produces the confirmed real-world JSON field skeleton for each theme item type, used as
/// the <see cref="Items.ThemeItem.RawJson"/> base when building a brand-new item from
/// scratch via <see cref="ThemeBuilder"/>/<see cref="ThemePageBuilder"/>.
///
/// Field sets were cross-checked against multiple real theme files shipped with
/// ScreenKeyWindows_v1_1 (not just one sample), specifically:
///   - theme/MK20/defaultTheme.Theme       (KeyItem w/ "paths" set + "frameDelays"; DynamicImageItem; TextItem; BackgroundItem "main"=mp4;
///                                           ProgressBarItem; RadialGaugeItem; LinearGaugeItem; DigitalClockItem)
///   - theme/MK20/时尚按键/时尚按键.Theme    (simple KeyItem grid with empty "paths"/"title"; second ProgressBarItem/TextItem variant)
/// as well as this project's own capture14 wire-level trace of a real theme upload.
///
/// Each skeleton intentionally includes every field observed across those samples so that
/// items built through this API are structurally indistinguishable from ones saved by the
/// real ScreenKeyWindows editor - omitting required fields (e.g. a KeyItem missing
/// "maxWidth"/"maxHeight"/"scaledWidthTo"/"scaledHeightTo"/"opacity"/"paths"/"soundFile"/
/// "title"/"titleParam", or carrying a spurious "w"/"h" instead) was confirmed on real
/// hardware to cause <c>SET_DEVICE_RELOAD</c> to hang - see PROTOCOL_WAVESHARE_MK20.md §7.1.
/// </summary>
internal static class ThemeItemSkeletons
{
    private const string DefaultTitleParam = """{"FontFamily":"Microsoft YaHei","FontSize":24,"FontStyle":"","FontUnderline":false,"ShowImage":true,"ShowTitle":true,"TitleAlignment":"bottom","TitleColor":"#ffffff"}""";

    public static JsonElement EmptyObject { get; } = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>
    /// KeyItem (type 115) skeleton. <paramref name="maxWidth"/>/<paramref name="maxHeight"/>
    /// are the cell's bounds (typically the canvas width/height for a full-page key grid);
    /// <paramref name="scaledWidthTo"/>/<paramref name="scaledHeightTo"/> is the rendered
    /// icon size (128x128 in every sample observed).
    /// </summary>
    public static JsonElement KeyItem(
        double maxWidth, double maxHeight,
        double scaledWidthTo = 128, double scaledHeightTo = 128,
        string title = "", string? titleParam = null,
        string opacity = "100", string paths = "", string soundFile = "",
        string? frameDelays = null)
    {
        var obj = new JsonObject
        {
            ["maxWidth"] = maxWidth.ToString(),
            ["maxHeight"] = maxHeight.ToString(),
            ["opacity"] = opacity,
            // Confirmed real animated keys always have "path":"" explicitly (not omitted)
            // alongside a non-empty "paths" folder - static keys instead get "path"
            // overwritten with their actual icon asset path by ThemeFileCodec.BuildItemJson
            // when KeyItem.IconAssetPath is set.
            ["path"] = "",
            ["paths"] = paths,
            ["scaledWidthTo"] = scaledWidthTo.ToString(),
            ["scaledHeightTo"] = scaledHeightTo.ToString(),
            ["soundFile"] = soundFile,
            ["title"] = title,
            ["titleParam"] = titleParam ?? DefaultTitleParam,
        };
        if (frameDelays is not null) obj["frameDelays"] = frameDelays;
        return ToElement(obj);
    }

    /// <summary>
    /// BackgroundItem (type 100) skeleton. Real background items carry both "w"/"h" (via
    /// the base <c>ThemeItem.Width</c>/<c>Height</c> typed properties, overlaid separately by
    /// <c>ThemeFileCodec.BuildItemJson</c>) AND "maxWidth"/"maxHeight" together - unlike key
    /// items, which never have "w"/"h".
    /// </summary>
    public static JsonElement BackgroundItem(double maxWidth, double maxHeight)
    {
        var obj = new JsonObject
        {
            ["maxWidth"] = maxWidth.ToString(),
            ["maxHeight"] = maxHeight.ToString(),
        };
        return ToElement(obj);
    }

    /// <summary>DynamicImageItem (type 114, animated GIF) skeleton.</summary>
    public static JsonElement DynamicImageItem(double maxWidth, double maxHeight)
    {
        var obj = new JsonObject
        {
            ["maxWidth"] = maxWidth.ToString(),
            ["maxHeight"] = maxHeight.ToString(),
            ["paths"] = "",
        };
        return ToElement(obj);
    }

    /// <summary>TextItem (type 113) skeleton. <paramref name="font"/> matches the observed "family,size,-1,5,weight,0,0,0,0,0[,style]" descriptor shape.</summary>
    public static JsonElement TextItem(string frontColorRgba, string font)
    {
        var obj = new JsonObject
        {
            ["front_color"] = frontColorRgba,
            ["text_customFont_flag"] = "",
            ["text_customFont_path"] = "",
            ["text_font"] = font,
        };
        return ToElement(obj);
    }

    /// <summary>ProgressBarItem (type 102) skeleton - a circular/linear bar with gradient-capable border/front/back colors.</summary>
    public static JsonElement ProgressBarItem(string frontColorRgba, string backColorRgba, string borderColorRgba, double borderWidth, double cornerRadius)
    {
        var obj = new JsonObject
        {
            ["front_color"] = frontColorRgba,
            ["back_color"] = backColorRgba,
            ["border_color"] = borderColorRgba,
            ["border_width"] = borderWidth.ToString(),
            ["corner_radius"] = cornerRadius.ToString(),
            ["lineargradient_flag"] = "0",
            ["lineargradient_color"] = "r=000,g=000,b=255,a=255",
        };
        return ToElement(obj);
    }

    /// <summary>LinearGaugeItem (type 103) skeleton - solid front/back/border colors, no gradient.</summary>
    public static JsonElement LinearGaugeItem(string frontColorRgba, string backColorRgba, string borderColorRgba, double borderWidth)
    {
        var obj = new JsonObject
        {
            ["front_color"] = frontColorRgba,
            ["back_color"] = backColorRgba,
            ["border_color"] = borderColorRgba,
            ["border_width"] = borderWidth.ToString(),
        };
        return ToElement(obj);
    }

    /// <summary>RadialGaugeItem (type 109) skeleton - arc gauge with up to 3 gradient stops.</summary>
    public static JsonElement RadialGaugeItem(double radius)
    {
        var obj = new JsonObject
        {
            ["radius"] = radius.ToString(),
        };
        return ToElement(obj);
    }

    /// <summary>
    /// DigitalClockItem (type 111) skeleton. A full clock display is composed of 2-3
    /// adjacent items (one per <paramref name="systemDataName"/> field: "hour"/"minute"/
    /// "second"), matching the observed pattern in defaultTheme.Theme.
    /// </summary>
    public static JsonElement DigitalClockItem(string frontColorRgba, string backColorRgba, string borderColorRgba, string font, int displayNum = 2)
    {
        var obj = new JsonObject
        {
            ["front_color"] = frontColorRgba,
            ["back_color"] = backColorRgba,
            ["border_color"] = borderColorRgba,
            ["border_width"] = "0",
            ["corner_radius"] = "0",
            ["displayNum"] = displayNum.ToString(),
            ["displayType"] = "0",
            ["paths"] = "",
            ["text_customFont_flag"] = "",
            ["text_customFont_path"] = "",
            ["text_font"] = font,
            ["transition"] = "0",
        };
        return ToElement(obj);
    }

    private static JsonElement ToElement(JsonObject obj)
    {
        using var doc = JsonDocument.Parse(obj.ToJsonString());
        return doc.RootElement.Clone();
    }
}
