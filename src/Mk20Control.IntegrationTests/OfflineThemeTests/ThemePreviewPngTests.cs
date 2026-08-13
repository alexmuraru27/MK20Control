using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Generates a thumbnail PNG for a theme - ScreenKeyWindows requires a matching <c>.png</c>
/// file alongside a <c>.Theme</c> file to show it in its theme library thumbnail list; exact
/// pixel content is not load-bearing for the device itself, only for the vendor UI's
/// thumbnail. No hardware required. Formerly <c>Mk20Control.App</c>'s
/// <c>GeneratePreviewPng</c>/<c>GenerateRealBackgroundPreviewPng</c>.
/// </summary>
public class ThemePreviewPngTests
{
    /// <summary>Generates a simple 640x656 preview PNG showing the given icon numbers in a row.</summary>
    public static byte[] GeneratePreviewPng(params int[] iconNums)
    {
        using var canvas = new Image<Rgb24>(640, 656, Color.Black);
        for (int i = 0; i < iconNums.Length; i++)
        {
            string iconFile = TestPaths.IconFile(iconNums[i]);
            if (!File.Exists(iconFile)) continue;
            using var icon = Image.Load<Rgb24>(iconFile);
            icon.Mutate(x => x.Resize(128, 128));
            canvas.Mutate(x => x.DrawImage(icon, new Point(i * 128, 144), 1f));
        }
        using var ms = new MemoryStream();
        canvas.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    /// <summary>Like <see cref="GeneratePreviewPng"/>, but composites the icons on top of a real main-screen background image for a more representative thumbnail.</summary>
    public static byte[] GenerateRealBackgroundPreviewPng(string mainBackgroundImagePath, params int[] iconNums)
    {
        using var bgFrame = Image.Load<Rgb24>(mainBackgroundImagePath);
        bgFrame.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(640, 512), Mode = ResizeMode.Crop }));

        using var canvas = new Image<Rgb24>(640, 656, Color.Black);
        canvas.Mutate(x => x.DrawImage(bgFrame, new Point(0, 144), 1f));

        for (int i = 0; i < iconNums.Length; i++)
        {
            string iconFile = TestPaths.IconFile(iconNums[i]);
            if (!File.Exists(iconFile)) continue;
            using var icon = Image.Load<Rgb24>(iconFile);
            icon.Mutate(x => x.Resize(128, 128));
            canvas.Mutate(x => x.DrawImage(icon, new Point(i * 128, 144), 1f));
        }
        using var ms = new MemoryStream();
        canvas.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    [Test]
    public void GeneratePreviewPng_ProducesValidPng()
    {
        byte[] png = GeneratePreviewPng(2, 3, 4, 5, 8);

        Assert.That(png, Is.Not.Empty);
        using var decoded = Image.Load(png);
        Assert.That(decoded.Width, Is.EqualTo(640));
        Assert.That(decoded.Height, Is.EqualTo(656));

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-title-opacity-demo-theme.png");
        File.WriteAllBytes(outPath, png);
        TestContext.WriteLine($"Wrote preview to {outPath}");
    }

    [Test]
    public void GenerateRealBackgroundPreviewPng_ProducesValidPng()
    {
        string bgPath = TestPaths.BackgroundFile("Racing_Setup_Cheatsheet.jpg");
        Assert.That(File.Exists(bgPath), Is.True, $"Missing background: {bgPath}");

        byte[] png = GenerateRealBackgroundPreviewPng(bgPath, 1, 2, 3, 4, 5);

        Assert.That(png, Is.Not.Empty);
        using var decoded = Image.Load(png);
        Assert.That(decoded.Width, Is.EqualTo(640));
        Assert.That(decoded.Height, Is.EqualTo(656));

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-title-opacity-backgrounds-demo-theme.png");
        File.WriteAllBytes(outPath, png);
        TestContext.WriteLine($"Wrote preview to {outPath}");
    }
}
