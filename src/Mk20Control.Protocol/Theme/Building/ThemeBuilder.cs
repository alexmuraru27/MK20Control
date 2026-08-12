using System;
using System.Collections.Generic;
using System.Linq;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Items;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Fluent builder for a complete <see cref="ThemeFile"/> from scratch - the primary
/// programmatic entry point for "set a picture on a button and make it do X", "set a
/// full-screen background", "add a data gauge", etc. without hand-writing any JSON.
///
/// Produces items whose JSON skeleton was cross-checked against multiple real theme files
/// (see <see cref="ThemeItemSkeletons"/> remarks and PROTOCOL_WAVESHARE_MK20.md §7) so themes
/// built here are structurally indistinguishable from ones saved by the real ScreenKeyWindows
/// editor. Call <see cref="Build"/> to obtain the immutable <see cref="ThemeFile"/>, then
/// encode it with <c>Mk20Control.Protocol.Codecs.ThemeFileCodec.Encode</c>.
///
/// Example:
/// <code>
/// var theme = new ThemeBuilder()
///     .AddPage(page => page
///         .SetCanvas(640, 656)
///         .AddBackground(bg => bg.MainScreen("bg.png", backgroundPngBytes))
///         .AddKey(0, 0, key => key
///             .Icon("icon_01.png", icon01Bytes)
///             .Action(KeyActions.Keyboard(0x1E, "1"))))
///     .Build();
/// byte[] bytes = ThemeFileCodec.Encode(theme);
/// </code>
/// </summary>
public sealed class ThemeBuilder : IThemeAssetRegistry
{
    private readonly List<ThemePageBuilder> _pages = new();
    private readonly Dictionary<string, ThemeAsset> _assets = new();
    private int _nextItemId = 1;
    private int _nextAssetSeq = 1;

    public int Language { get; set; } = 0;
    public string LayoutVersion { get; set; } = "V3.0";

    /// <summary>Adds a new page, configured via <paramref name="configure"/>, and returns this builder for chaining.</summary>
    public ThemeBuilder AddPage(Action<ThemePageBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var page = new ThemePageBuilder(this);
        configure(page);
        _pages.Add(page);
        return this;
    }

    /// <summary>Adds a new page and returns its builder directly (alternative to the <see cref="Action{T}"/> overload for callers who prefer imperative chaining).</summary>
    public ThemePageBuilder AddPage()
    {
        var page = new ThemePageBuilder(this);
        _pages.Add(page);
        return page;
    }

    /// <summary>
    /// Registers an asset under a stable, collision-free virtual path derived from
    /// <paramref name="suggestedFileName"/> and returns the path to reference from an item
    /// (e.g. <c>KeyItem.IconAssetPath</c>). If the same (path, bytes) pair is registered
    /// twice, the existing path is reused rather than duplicating the asset.
    /// </summary>
    public string RegisterAsset(string suggestedFileName, byte[] data)
    {
        string path = $"/image/mk20control/{_nextAssetSeq:D4}_{suggestedFileName}";
        if (_assets.TryGetValue(path, out var existing) && existing.Data.AsSpan().SequenceEqual(data))
            return path;
        _nextAssetSeq++;
        _assets[path] = new ThemeAsset { Path = path, Data = data };
        return path;
    }

    public string AllocateItemId() => (_nextItemId++).ToString();

    /// <summary>Builds the immutable <see cref="ThemeFile"/>. The first added page becomes <see cref="ThemeFile.CurrentPageId"/>.</summary>
    public ThemeFile Build()
    {
        if (_pages.Count == 0)
            throw new InvalidOperationException("A theme must have at least one page.");

        var pages = _pages.Select(p => p.Build()).ToList();
        return new ThemeFile
        {
            Language = Language,
            KeyMacroValue = Array.Empty<byte>(),
            KeyMacro = null,
            CurrentPageId = pages[0].PageName ?? "",
            LayoutVersion = LayoutVersion,
            Pages = pages,
            Assets = _assets.Values.ToList(),
        };
    }
}
