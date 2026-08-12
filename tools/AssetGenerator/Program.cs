using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

// Generates placeholder test assets for the MK20 control project:
//   - 40 numbered 64x64 icon badges (assets/icons)
//   - a handful of full-canvas / main-screen / secondary-screen / encoder background
//     test images (assets/backgrounds), sized per the device geometry documented in
//     PROTOCOL_WAVESHARE_MK20.md (section 6) and the official Waveshare MK20 wiki
//     ("Theme Customization" section):
//       Button (key)      : 128 x 128
//       Main screen (20 keys): 640 x 512   [wiki]
//       Secondary screen  : 428 x 142      [wiki]
//       Encoder           : 214 x 142      [wiki]
//       Full device canvas: 640 x 656      [protocol doc §6, inferred: 512 key grid + ~144 secondary band]
//
// All artwork here is procedurally generated (gradients/shapes/text) - no third-party
// or copyrighted images are used, so it is safe to keep alongside the control project.

string repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string iconsDir = System.IO.Path.Combine(repoRoot, "assets", "icons");
string backgroundsDir = System.IO.Path.Combine(repoRoot, "assets", "backgrounds");
Directory.CreateDirectory(iconsDir);
Directory.CreateDirectory(backgroundsDir);

var font = SystemFonts.CreateFont(PickFontFamily(), 22, FontStyle.Bold);

GenerateIcons(iconsDir, font);
GenerateBackgrounds(backgroundsDir);

Console.WriteLine($"Icons written to:       {iconsDir}");
Console.WriteLine($"Backgrounds written to: {backgroundsDir}");

static string PickFontFamily()
{
    // Prefer a common Windows font; fall back to whatever is first available.
    string[] preferred = { "Segoe UI", "Arial", "Verdana", "Tahoma" };
    foreach (var name in preferred)
    {
        if (SystemFonts.Collection.TryGet(name, out _)) return name;
    }
    foreach (var fam in SystemFonts.Collection.Families) return fam.Name;
    throw new InvalidOperationException("No system fonts found.");
}

static void GenerateIcons(string iconsDir, Font font)
{
    const int size = 64;
    const int count = 40;

    for (int i = 1; i <= count; i++)
    {
        double hue = (i - 1) / (double)count * 360.0;
        var bg = ColorFromHsv(hue, 0.55, 0.85);
        var fg = ColorFromHsv(hue, 0.75, 0.35);

        using var img = new Image<Rgba32>(size, size);
        img.Mutate(ctx =>
        {
            ctx.Fill(bg);

            // Shape varies every 4 icons so the badges are visually distinguishable in a grid.
            int shapeKind = (i - 1) % 4;
            float pad = 6f;
            var rect = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);
            switch (shapeKind)
            {
                case 0: // circle
                    ctx.Fill(fg, new EllipsePolygon(size / 2f, size / 2f, size / 2f - pad));
                    break;
                case 1: // square
                    ctx.Fill(fg, new RectangularPolygon(rect.X, rect.Y, rect.Width, rect.Height));
                    break;
                case 2: // diamond
                    ctx.Fill(fg, new Polygon(new LinearLineSegment(
                        new PointF(size / 2f, pad),
                        new PointF(size - pad, size / 2f),
                        new PointF(size / 2f, size - pad),
                        new PointF(pad, size / 2f))));
                    break;
                default: // hexagon
                    ctx.Fill(fg, BuildHexagon(size / 2f, size / 2f, size / 2f - pad));
                    break;
            }

            string label = i.ToString();
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(size / 2f, size / 2f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ctx.DrawText(textOptions, label, Color.White);
        });

        img.SaveAsPng(System.IO.Path.Combine(iconsDir, $"icon_{i:D2}.png"));
    }
}

static IPath BuildHexagon(float cx, float cy, float r)
{
    var points = new PointF[6];
    for (int k = 0; k < 6; k++)
    {
        double angle = Math.PI / 3 * k - Math.PI / 2;
        points[k] = new PointF(cx + (float)(r * Math.Cos(angle)), cy + (float)(r * Math.Sin(angle)));
    }
    return new Polygon(new LinearLineSegment(points));
}

/// <summary>
/// Builds a thin quad (as a fillable IPath) representing a straight line segment.
/// Used instead of the DrawLines() extension so this generator has no dependency on a
/// specific ImageSharp.Drawing minor-version API surface beyond Fill()/Polygon/LinearLineSegment.
/// </summary>
static IPath BuildLine(PointF p1, PointF p2, float thickness)
{
    float dx = p2.X - p1.X;
    float dy = p2.Y - p1.Y;
    float len = MathF.Max(0.0001f, MathF.Sqrt(dx * dx + dy * dy));
    float nx = -dy / len * (thickness / 2f);
    float ny = dx / len * (thickness / 2f);
    return new Polygon(new LinearLineSegment(
        new PointF(p1.X + nx, p1.Y + ny),
        new PointF(p2.X + nx, p2.Y + ny),
        new PointF(p2.X - nx, p2.Y - ny),
        new PointF(p1.X - nx, p1.Y - ny)));
}

static Color ColorFromHsv(double h, double s, double v)
{
    double c = v * s;
    double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
    double m = v - c;
    (double r, double g, double b) = h switch
    {
        < 60 => (c, x, 0.0),
        < 120 => (x, c, 0.0),
        < 180 => (0.0, c, x),
        < 240 => (0.0, x, c),
        < 300 => (x, 0.0, c),
        _ => (c, 0.0, x),
    };
    return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
}

static void GenerateBackgrounds(string backgroundsDir)
{
    var sizes = new (string Label, int W, int H)[]
    {
        ("full_canvas", 640, 656),
        ("main_screen", 640, 512),
        ("secondary_screen", 428, 142),
        ("encoder", 214, 142),
    };

    foreach (var (label, w, h) in sizes)
    {
        DrawGradient(backgroundsDir, label, w, h);
        DrawGridTestPattern(backgroundsDir, label, w, h);
        DrawCarbonDark(backgroundsDir, label, w, h);
        DrawColorBars(backgroundsDir, label, w, h);
    }
}

static void DrawGradient(string dir, string label, int w, int h)
{
    using var img = new Image<Rgba32>(w, h);
    var top = ColorFromHsv(210, 0.65, 0.95);
    var bottom = ColorFromHsv(280, 0.65, 0.35);
    img.Mutate(ctx =>
    {
        var brush = new LinearGradientBrush(
            new PointF(0, 0), new PointF(0, h),
            GradientRepetitionMode.None,
            new ColorStop(0f, top), new ColorStop(1f, bottom));
        ctx.Fill(brush);
    });
    img.SaveAsPng(System.IO.Path.Combine(dir, $"gradient_{label}_{w}x{h}.png"));
}

static void DrawGridTestPattern(string dir, string label, int w, int h)
{
    using var img = new Image<Rgba32>(w, h);
    img.Mutate(ctx =>
    {
        ctx.Fill(Color.Black);
        int step = 32;
        var lineColor = Color.FromRgb(0, 200, 90);
        for (int x = 0; x <= w; x += step)
            ctx.Fill(lineColor, BuildLine(new PointF(x, 0), new PointF(x, h), 1f));
        for (int y = 0; y <= h; y += step)
            ctx.Fill(lineColor, BuildLine(new PointF(0, y), new PointF(w, y), 1f));

        // Crosshair + corner markers to help verify orientation/cropping once streamed to the device.
        ctx.Fill(Color.Red, BuildLine(new PointF(w / 2f, 0), new PointF(w / 2f, h), 2f));
        ctx.Fill(Color.Red, BuildLine(new PointF(0, h / 2f), new PointF(w, h / 2f), 2f));
        int m = 10;
        ctx.Fill(Color.Yellow, new RectangularPolygon(0, 0, m, m));
        ctx.Fill(Color.Cyan, new RectangularPolygon(w - m, 0, m, m));
        ctx.Fill(Color.Magenta, new RectangularPolygon(0, h - m, m, m));
        ctx.Fill(Color.White, new RectangularPolygon(w - m, h - m, m, m));
    });
    img.SaveAsPng(System.IO.Path.Combine(dir, $"grid_test_{label}_{w}x{h}.png"));
}

static void DrawCarbonDark(string dir, string label, int w, int h)
{
    using var img = new Image<Rgba32>(w, h);
    img.Mutate(ctx =>
    {
        ctx.Fill(Color.FromRgb(18, 18, 20));
        var stripe = Color.FromRgba(255, 255, 255, 12);
        for (int x = -h; x < w; x += 14)
        {
            ctx.Fill(stripe, BuildLine(new PointF(x, 0), new PointF(x + h, h), 6f));
        }
    });
    img.SaveAsPng(System.IO.Path.Combine(dir, $"carbon_dark_{label}_{w}x{h}.png"));
}

static void DrawColorBars(string dir, string label, int w, int h)
{
    using var img = new Image<Rgba32>(w, h);
    Color[] bars =
    {
        Color.White, Color.Yellow, Color.Cyan, Color.Lime,
        Color.Magenta, Color.Red, Color.Blue, Color.Black,
    };
    img.Mutate(ctx =>
    {
        float barWidth = w / (float)bars.Length;
        for (int i = 0; i < bars.Length; i++)
        {
            ctx.Fill(bars[i], new RectangularPolygon(i * barWidth, 0, barWidth + 1, h));
        }
    });
    img.SaveAsPng(System.IO.Path.Combine(dir, $"color_bars_{label}_{w}x{h}.png"));
}
