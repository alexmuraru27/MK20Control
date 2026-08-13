using Mk20Control.Protocol.Codecs;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Dumps the raw JSON of every distinct item type found in a <c>.Theme</c> file - a
/// diagnostic aid for inspecting real vendor themes or the output of the builder API. Uses
/// the theme built by <see cref="FiveKeyTestThemeTests"/> as a concrete, self-contained
/// example; pass a real file's bytes to <see cref="DumpRawJson"/> directly to inspect any
/// other theme. No hardware required. Formerly <c>Mk20Control.App</c>'s
/// <c>--dump-raw-json</c> CLI flag.
/// </summary>
public class DumpRawJsonTests
{
    /// <summary>Prints every distinct item type's raw JSON found in <paramref name="themeBytes"/> (only the first occurrence of each type code, unless <paramref name="dumpAll"/> is set).</summary>
    public static void DumpRawJson(byte[] themeBytes, bool dumpAll = false)
    {
        var theme = ThemeFileCodec.Decode(themeBytes);
        TestContext.WriteLine($"Pages: {theme.Pages.Count}, Assets: {theme.Assets.Count}, LayoutVersion: {theme.LayoutVersion}");

        var seenTypes = new HashSet<string>();
        foreach (var page in theme.Pages)
        {
            TestContext.WriteLine("Canvas: " + System.Text.Json.JsonSerializer.Serialize(page.Canvas));
            foreach (var item in page.Items)
            {
                if (!dumpAll && !seenTypes.Add(item.RawTypeCode)) continue;
                TestContext.WriteLine($"[type={item.RawTypeCode} / {item.GetType().Name}]: " + item.RawJson.GetRawText());
            }
        }
    }

    [Test]
    public void DumpRawJson_PrintsEveryItemType()
    {
        byte[] bytes = FiveKeyTestThemeTests.BuildFiveKeyTestTheme();
        DumpRawJson(bytes);
    }
}
