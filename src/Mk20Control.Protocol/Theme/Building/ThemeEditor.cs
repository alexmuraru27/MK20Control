using System;
using System.Collections.Generic;
using System.Linq;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Items;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Reader/writer for an existing <see cref="ThemeFile"/> (e.g. decoded from a real .Theme
/// file via <c>ThemeFileCodec.Decode</c>) - the complement to <see cref="ThemeBuilder"/> for
/// "load a real/previously-built theme and change just this one button's icon/action" style
/// edits, without having to reconstruct the whole file by hand.
///
/// All edits preserve every item's original <see cref="ThemeItem.RawJson"/> except for the
/// specific fields being changed (matching <c>ThemeFileCodec.BuildItemJson</c>'s merge-over
/// behavior), so round-trip fidelity for fields this library doesn't model is never lost.
///
/// Example:
/// <code>
/// var editor = new ThemeEditor(ThemeFileCodec.Decode(existingBytes));
/// editor.Page(0).SetKeyIcon(row: 0, column: 2, "new_icon.png", newIconBytes);
/// editor.Page(0).SetKeyAction(row: 0, column: 2, KeyActions.Keyboard(0x1E, "1"));
/// byte[] updatedBytes = ThemeFileCodec.Encode(editor.Save());
/// </code>
/// </summary>
public sealed class ThemeEditor : IThemeAssetRegistry
{
    private readonly List<PageEditor> _pages;
    private readonly Dictionary<string, ThemeAsset> _assets;
    private int _language;
    private byte[] _keyMacroValue;
    private byte[]? _keyMacro;
    private string _currentPageId;
    private string _layoutVersion;
    private int _nextItemId = 100000; // start high to avoid colliding with the source theme's own ids

    public ThemeEditor(ThemeFile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _language = source.Language;
        _keyMacroValue = source.KeyMacroValue;
        _keyMacro = source.KeyMacro;
        _currentPageId = source.CurrentPageId;
        _layoutVersion = source.LayoutVersion;
        _assets = source.Assets.ToDictionary(a => a.Path, a => a);
        _pages = source.Pages.Select(p => new PageEditor(this, p)).ToList();
    }

    /// <summary>Number of pages in this theme.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Gets the editor for the page at <paramref name="index"/> (0-based).</summary>
    public PageEditor Page(int index) => _pages[index];

    /// <summary>Gets the editor for the page whose id matches <paramref name="pageId"/>.</summary>
    public PageEditor? PageById(string pageId) => _pages.FirstOrDefault(p => p.PageId == pageId);

    /// <summary>Registers a new asset (or reuses an identical existing one by path+bytes) and returns its virtual path.</summary>
    public string RegisterAsset(string suggestedFileName, byte[] data)
    {
        // Confirmed via a real ScreenKeyWindows-created reference theme
        // (customTheme7buttonsSoftware.Theme, built entirely through their own UI): new icon
        // assets are registered under the same "/image/MK20/cache/<fileName>" namespace the
        // device/software already uses for its built-in icon library - NOT a separate,
        // library-invented namespace. Using an unfamiliar path prefix (e.g. the previous
        // "/image/mk20control/<guid>_<fileName>") was one of the confirmed structural
        // differences from a real, known-working theme - see PROTOCOL_WAVESHARE_MK20.md §10
        // Item #10.
        string path = $"/image/MK20/cache/{suggestedFileName}";
        if (_assets.TryGetValue(path, out var existing) && !existing.Data.AsSpan().SequenceEqual(data))
        {
            // Path collision with different bytes - fall back to a disambiguated name rather
            // than silently overwriting a different asset.
            string ext = System.IO.Path.GetExtension(suggestedFileName);
            string stem = System.IO.Path.GetFileNameWithoutExtension(suggestedFileName);
            path = $"/image/MK20/cache/{stem}_{Guid.NewGuid():N}{ext}";
        }
        _assets[path] = new ThemeAsset { Path = path, Data = data };
        return path;
    }

    /// <summary>See <see cref="IThemeAssetRegistry.RegisterAssetAtPath"/>.</summary>
    public string RegisterAssetAtPath(string fullPath, byte[] data)
    {
        _assets[fullPath] = new ThemeAsset { Path = fullPath, Data = data };
        return fullPath;
    }

    /// <summary>Allocates a new unique item id string, starting from a high number to avoid colliding with the source theme's existing item ids.</summary>
    public string AllocateItemId() => (_nextItemId++).ToString();

    /// <summary>Removes an asset by its virtual path (does not automatically clear item references to it - update those first).</summary>
    public bool RemoveAsset(string assetPath) => _assets.Remove(assetPath);

    /// <summary>Sets the active page shown when the theme is first loaded.</summary>
    public ThemeEditor SetCurrentPage(string pageId) { _currentPageId = pageId; return this; }

    /// <summary>Rebuilds the immutable <see cref="ThemeFile"/> reflecting all edits made so far.</summary>
    public ThemeFile Save() => new()
    {
        Language = _language,
        KeyMacroValue = _keyMacroValue,
        KeyMacro = _keyMacro,
        CurrentPageId = _currentPageId,
        LayoutVersion = _layoutVersion,
        Pages = _pages.Select(p => p.Build()).ToList(),
        Assets = _assets.Values.ToList(),
    };

    /// <summary>Editor for a single existing page's items.</summary>
    public sealed class PageEditor
    {
        private readonly ThemeEditor _owner;
        private readonly List<ThemeItem> _items;
        private ThemeCanvas _canvas;
        private System.Text.Json.JsonElement? _encoder;

        public string? PageId { get; private set; }

        internal PageEditor(ThemeEditor owner, ThemePage source)
        {
            _owner = owner;
            PageId = source.PageName;
            _canvas = source.Canvas;
            _items = source.Items.ToList();
            _encoder = source.Encoder;
        }

        /// <summary>All items currently on this page, in original order.</summary>
        public IReadOnlyList<ThemeItem> Items => _items;

        /// <summary>Finds the key item at the given zero-based matrix row/column, or null if none exists there.</summary>
        public KeyItem? FindKey(int row, int column) =>
            _items.OfType<KeyItem>().FirstOrDefault(k => k.Row == row && k.Column == column);

        /// <summary>Replaces the icon of the key at (<paramref name="row"/>, <paramref name="column"/>) by registering a new asset (automatically normalized to the confirmed real-hardware key icon format, 128x128 RGB PNG). Throws if no key exists there.</summary>
        public PageEditor SetKeyIcon(int row, int column, string suggestedFileName, byte[] pngOrGifBytes)
        {
            var key = FindKey(row, column) ?? throw new InvalidOperationException($"No key at row={row}, column={column}.");
            string assetPath = _owner.RegisterAsset(suggestedFileName, IconImageNormalizer.NormalizeToKeyIcon(pngOrGifBytes));
            ReplaceItem(key, key with { IconAssetPath = assetPath });
            return this;
        }

        /// <summary>Replaces the assigned action of the key at (<paramref name="row"/>, <paramref name="column"/>). Throws if no key exists there.</summary>
        public PageEditor SetKeyAction(int row, int column, KeyAction action)
        {
            var key = FindKey(row, column) ?? throw new InvalidOperationException($"No key at row={row}, column={column}.");
            ReplaceItem(key, key with { Action = action, RawControlDataBase64 = null });
            return this;
        }

        /// <summary>Sets the on-screen title/label text of the key at (<paramref name="row"/>, <paramref name="column"/>). Throws if no key exists there.</summary>
        public PageEditor SetKeyTitle(int row, int column, string title)
        {
            var key = FindKey(row, column) ?? throw new InvalidOperationException($"No key at row={row}, column={column}.");
            using var doc = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.Nodes.JsonNode.Parse(key.RawJson.GetRawText())!.AsObject()
                    .Also(o => o["title"] = title).ToJsonString());
            ReplaceItem(key, key with { RawJson = doc.RootElement.Clone() });
            return this;
        }

        /// <summary>
        /// Sets the opacity/transparency of the key at (<paramref name="row"/>, <paramref
        /// name="column"/>), from 0 (fully transparent) to 100 (fully opaque, the default).
        /// Confirmed via a real ScreenKeyWindows capture
        /// (tools/Captures/capture19_text_over_buttons_and_txtinput.pcapng): making an
        /// icon translucent so a title reads clearly over it produces
        /// <c>"opacity":"15"</c> on that same key item - throws if no key exists there.
        /// </summary>
        public PageEditor SetKeyOpacity(int row, int column, int opacityPercent)
        {
            var key = FindKey(row, column) ?? throw new InvalidOperationException($"No key at row={row}, column={column}.");
            using var doc = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.Nodes.JsonNode.Parse(key.RawJson.GetRawText())!.AsObject()
                    .Also(o => o["opacity"] = opacityPercent.ToString()).ToJsonString());
            ReplaceItem(key, key with { RawJson = doc.RootElement.Clone() });
            return this;
        }

        /// <summary>Adds a brand-new key at the given position - use when a theme needs more keys than it originally had (e.g. converting a smaller layout).</summary>
        public PageEditor AddKey(int row, int column, Action<KeyItemBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            var b = new KeyItemBuilder(_owner, row, column, _canvas.Width ?? 640, _canvas.Height ?? 656);
            configure(b);
            _items.Add(b.Build());
            return this;
        }

        /// <summary>Removes the key at the given position, if one exists.</summary>
        public PageEditor RemoveKey(int row, int column)
        {
            var key = FindKey(row, column);
            if (key is not null) _items.Remove(key);
            return this;
        }

        /// <summary>Replaces the main-screen background asset (adds one if none existed).</summary>
        public PageEditor SetMainBackground(string suggestedFileName, byte[] imageOrGifOrMp4Bytes)
        {
            string assetPath = _owner.RegisterAsset(suggestedFileName, imageOrGifOrMp4Bytes);
            var existing = _items.OfType<BackgroundItem>().FirstOrDefault(b => b.Surface == BackgroundSurface.Main);
            if (existing is not null)
            {
                ReplaceItem(existing, existing with { AssetPath = assetPath });
            }
            else
            {
                double w = _canvas.Width ?? 640;
                double h = (_canvas.Height ?? 656) - 144;
                _items.Add(new BackgroundItem
                {
                    RawTypeCode = "100", Id = Guid.NewGuid().ToString("N")[..8], X = 0, Y = 144, Z = -2,
                    Width = w, Height = h, Rotate = 0, Scale = 1, IsLocked = true,
                    Surface = BackgroundSurface.Main, RawSurface = "main", AssetPath = assetPath,
                    RawJson = ThemeItemSkeletons.BackgroundItem(w, h),
                });
            }
            return this;
        }

        private void ReplaceItem(ThemeItem oldItem, ThemeItem newItem)
        {
            int idx = _items.IndexOf(oldItem);
            if (idx >= 0) _items[idx] = newItem;
        }

        internal ThemePage Build() => new() { PageName = PageId, Canvas = _canvas, Items = _items.ToList(), Encoder = _encoder };
    }
}

internal static class JsonNodeExtensions
{
    public static System.Text.Json.Nodes.JsonObject Also(this System.Text.Json.Nodes.JsonObject obj, Action<System.Text.Json.Nodes.JsonObject> action)
    {
        action(obj);
        return obj;
    }
}
