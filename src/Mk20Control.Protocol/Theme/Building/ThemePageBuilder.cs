using System;
using System.Collections.Generic;
using System.Linq;
using Mk20Control.Protocol.Theme.Items;
using Mk20Control.Protocol.Theme.Building.Widgets;

namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Fluent builder for a single <see cref="ThemePage"/> - a full-screen canvas plus its items
/// (keys, backgrounds, gauges, text, clock fields, dynamic images). Obtained from
/// <see cref="ThemeBuilder.AddPage()"/>.
/// </summary>
public sealed class ThemePageBuilder
{
    private readonly ThemeBuilder _owner;
    private readonly List<ThemeItem> _items = new();
    private double _canvasWidth = 640;
    private double _canvasHeight = 656;
    private bool _showUnit = true;

    /// <summary>This page's unique id (a GUID by convention, matching real themes) - reference this from <c>KeyActions.OpenPage</c> for folder-style navigation.</summary>
    public string PageId { get; }

    internal ThemePageBuilder(ThemeBuilder owner)
    {
        _owner = owner;
        PageId = Guid.NewGuid().ToString();
    }

    /// <summary>Sets the canvas size (defaults to 640x656, the confirmed MK20 main-screen canvas) - call before adding items that use it (e.g. a full-bleed background).</summary>
    public ThemePageBuilder SetCanvas(double width, double height, bool showUnit = true)
    {
        _canvasWidth = width;
        _canvasHeight = height;
        _showUnit = showUnit;
        return this;
    }

    /// <summary>Adds a background item (type 100), configured via <paramref name="configure"/>.</summary>
    public ThemePageBuilder AddBackground(Action<BackgroundItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new BackgroundItemBuilder(_owner, _canvasWidth, _canvasHeight);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>
    /// Adds a key item (type 115) at the given zero-based matrix row/column, configured via
    /// <paramref name="configure"/>. Position (x/y) is auto-derived from row/column assuming
    /// 128x128 cells starting at the confirmed key-grid origin (0, 144) unless overridden via
    /// <see cref="KeyItemBuilder.At"/>.
    /// </summary>
    public ThemePageBuilder AddKey(int row, int column, Action<KeyItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new KeyItemBuilder(_owner, row, column, _canvasWidth, _canvasHeight);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds a static or data-bound text item (type 113).</summary>
    public ThemePageBuilder AddText(Action<TextItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new TextItemBuilder(_owner);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds a data-bound progress bar item (type 102).</summary>
    public ThemePageBuilder AddProgressBar(Action<ProgressBarItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new ProgressBarItemBuilder(_owner);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds a data-bound linear gauge item (type 103).</summary>
    public ThemePageBuilder AddLinearGauge(Action<LinearGaugeItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new LinearGaugeItemBuilder(_owner);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds a data-bound radial/arc gauge item (type 109).</summary>
    public ThemePageBuilder AddRadialGauge(Action<RadialGaugeItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new RadialGaugeItemBuilder(_owner);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds a data-bound plain circular gauge item (type 101, solid ring, no gradient/angle range).</summary>
    public ThemePageBuilder AddCircularGauge(Action<CircularGaugeItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new CircularGaugeItemBuilder(_owner, segmented: false);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds a data-bound segmented/notched circular gauge item (type 104, "seg-circular" - same fields as <see cref="AddCircularGauge"/>, different render style).</summary>
    public ThemePageBuilder AddSegmentedCircularGauge(Action<CircularGaugeItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new CircularGaugeItemBuilder(_owner, segmented: true);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds a data-bound "light-shadow" ring gauge item (type 110, arc stroke + glow highlight).</summary>
    public ThemePageBuilder AddLightShadowGauge(Action<LightShadowGaugeItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new LightShadowGaugeItemBuilder(_owner);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds a static or data-bound multi-line (wrapping) text item (type 116).</summary>
    public ThemePageBuilder AddMultilineText(Action<MultilineTextItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new MultilineTextItemBuilder(_owner);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds a static or data-bound drop-shadow text item (type 117, border stroke + shadow).</summary>
    public ThemePageBuilder AddShadowText(Action<ShadowTextItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new ShadowTextItemBuilder(_owner);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds one digital-clock field item (type 111) - "hour"/"minute"/"second"; combine 2-3 adjacent items for a full clock, matching the observed real-theme pattern.</summary>
    public ThemePageBuilder AddDigitalClockField(Action<DigitalClockItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new DigitalClockItemBuilder(_owner);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    /// <summary>Adds an animated GIF item (type 114).</summary>
    public ThemePageBuilder AddDynamicImage(Action<DynamicImageItemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new DynamicImageItemBuilder(_owner, _canvasWidth, _canvasHeight);
        configure(b);
        _items.Add(b.Build());
        return this;
    }

    internal ThemePage Build() => new()
    {
        PageName = PageId,
        Canvas = new ThemeCanvas { Width = _canvasWidth, Height = _canvasHeight, IsFlipped = false, IsRotated = false, ShowUnit = _showUnit },
        Items = _items.ToList(),
    };
}
