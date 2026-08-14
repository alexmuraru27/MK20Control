using Mk20Control.IntegrationTests.Support;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Demonstrates every confirmed way to move between pages on an MK20, in one theme:
///
/// <list type="bullet">
/// <item><b>Relative paging</b> - <see cref="KeyActions.PreviousPage"/>/<see cref="KeyActions.NextPage"/>
/// (<c>pageSwitchMode</c> 1/2), always relative to whichever page is shown, so a set of
/// pages wired this way forms a ring.</item>
/// <item><b>Absolute jump</b> - <see cref="KeyActions.JumpToPage"/> (<c>pageSwitchMode=0</c>
/// plus a zero-based <c>jumpToPage</c> index), the hub-and-spoke style the vendor's own
/// <c>defaultTheme.Theme</c> uses exclusively.</item>
/// <item><b>Folder in</b> - <see cref="KeyActions.OpenPage"/>, targeting another page's
/// <see cref="ThemePageBuilder.PageId"/> GUID.</item>
/// <item><b>Folder out</b> - <see cref="KeyActions.OneLevelUp"/>, which always uses the fixed
/// sentinel <c>pageName="parentPage"</c>; the device pops a runtime navigation stack rather
/// than reading a parent declared in the file, which is why the same folder page can be
/// entered from several places and still return correctly.</item>
/// </list>
///
/// Layout (4x5 grid, the confirmed real MK20 main-screen size):
/// <code>
/// page 0  HUB      r0c0 jump->1   r0c1 jump->2   r0c2 openPage->folder(page 3)
/// page 1  RING A   r3c0 prev      r3c4 next      r0c0 jump->0 (home)
/// page 2  RING B   r3c0 prev      r3c4 next      r0c0 jump->0 (home)
/// page 3  FOLDER   r3c4 oneLevelUp (back out)
/// </code>
///
/// Pages 1 and 2 carry both relative paging AND a home jump, showing the two styles coexist
/// on one page. No hardware required; see <c>HardwareTests.NavigationThemeUploadTests</c>
/// for the upload variant.
/// </summary>
public class NavigationThemeBuilderTests
{
    public const int Rows = 4, Cols = 5;

    /// <summary>Index of the hub/home page - what the "home" keys jump back to.</summary>
    public const int HubPageIndex = 0;

    public const int RingAPageIndex = 1;
    public const int RingBPageIndex = 2;
    public const int FolderPageIndex = 3;

    public static byte[] BuildNavigationTheme()
    {
        byte[] icon(int n)
        {
            string path = TestPaths.IconFile(n);
            Assert.That(File.Exists(path), Is.True, $"Missing icon file: {path}. Run tools\\AssetGenerator first.");
            return File.ReadAllBytes(path);
        }

        var builder = new ThemeBuilder();

        // The folder page's id must be known before the hub page can open it, so create all
        // page builders up front and configure them afterwards (AddPage() returns the
        // builder, unlike the Action<T> overload).
        var hub = builder.AddPage();
        var ringA = builder.AddPage();
        var ringB = builder.AddPage();
        var folder = builder.AddPage();

        hub.SetCanvas(640, 656);
        ringA.SetCanvas(640, 656);
        ringB.SetCanvas(640, 656);
        folder.SetCanvas(640, 656);

        // Declaring the parent is what makes this page a FOLDER rather than an ordinary page.
        // Without it the device enters via openPage but the oneLevelUp key does nothing.
        folder.AsFolderOf(hub);

        // --- Page 0: hub. Absolute jumps out to each ring page, plus a folder. ---
        hub.AddKey(0, 0, key => key
            .Icon("icon_01.png", icon(1))
            .Title("PAGE 1")
            .Action(KeyActions.JumpToPage(RingAPageIndex)));
        hub.AddKey(0, 1, key => key
            .Icon("icon_02.png", icon(2))
            .Title("PAGE 2")
            .Action(KeyActions.JumpToPage(RingBPageIndex)));
        hub.AddKey(0, 2, key => key
            .IconDevice(DeviceIcon.OpenFolder)
            .Title("FOLDER")
            .Action(KeyActions.OpenPage(folder.PageId)));

        // --- Pages 1 and 2: relative paging ring, plus an absolute jump home. ---
        foreach (var (page, iconNumber, label) in new[] { (ringA, 3, "RING A"), (ringB, 4, "RING B") })
        {
            page.AddKey(0, 0, key => key
                .Icon($"icon_{iconNumber:D2}.png", icon(iconNumber))
                .Title(label)
                .Action(KeyActions.Keyboard(HidKey.A, "A")));
            page.AddKey(0, 4, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("HOME")
                .Action(KeyActions.JumpToPage(HubPageIndex)));
            page.AddKey(Rows - 1, 0, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("PREV")
                .Action(KeyActions.PreviousPage()));
            page.AddKey(Rows - 1, Cols - 1, key => key
                .IconDevice(DeviceIcon.PageSwitch)
                .Title("NEXT")
                .Action(KeyActions.NextPage()));
        }

        // --- Page 3: the folder. Bottom-right returns to whatever opened it. ---
        folder.AddKey(0, 0, key => key
            .Icon("icon_05.png", icon(5))
            .Title("IN FOLDER")
            .Action(KeyActions.Keyboard(HidKey.B, "B")));
        folder.AddKey(Rows - 1, Cols - 1, key => key
            .IconDevice(DeviceIcon.OneLevelUp)
            .Title("BACK")
            .Action(KeyActions.OneLevelUp()));

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BuildNavigationTheme_RoundTripsCorrectly()
    {
        byte[] encoded = BuildNavigationTheme();
        var decoded = ThemeFileCodec.Decode(encoded);

        Assert.That(decoded.Pages, Has.Count.EqualTo(4));

        var keysByPage = decoded.Pages
            .Select(p => p.Items.OfType<KeyItem>().ToList())
            .ToList();

        // Hub: two absolute jumps and one folder-open.
        var hubJumps = keysByPage[HubPageIndex]
            .Select(k => k.Action).OfType<PageSwitchAction>()
            .Where(a => a.PageSwitchMode == 0).ToList();
        Assert.That(hubJumps.Select(a => a.JumpToPage), Is.EquivalentTo(new[] { RingAPageIndex, RingBPageIndex }));

        var openPage = keysByPage[HubPageIndex].Select(k => k.Action).OfType<OpenPageAction>().Single();
        Assert.That(openPage.PageName, Is.EqualTo(decoded.Pages[FolderPageIndex].PageName));
        Assert.That(openPage.PageName, Is.Not.Null.And.Not.EqualTo("parentPage"));

        // Ring pages: previous + next + a home jump each.
        foreach (int pageIndex in new[] { RingAPageIndex, RingBPageIndex })
        {
            var switches = keysByPage[pageIndex].Select(k => k.Action).OfType<PageSwitchAction>().ToList();
            Assert.That(switches.Count(a => a.PageSwitchMode == 1), Is.EqualTo(1), $"page {pageIndex} previous");
            Assert.That(switches.Count(a => a.PageSwitchMode == 2), Is.EqualTo(1), $"page {pageIndex} next");
            Assert.That(
                switches.Where(a => a.PageSwitchMode == 0).Select(a => a.JumpToPage),
                Is.EquivalentTo(new[] { HubPageIndex }),
                $"page {pageIndex} home jump");
        }

        // Folder page: exactly one oneLevelUp, at the confirmed bottom-right cell.
        var backKey = keysByPage[FolderPageIndex].Single(k => k.Action is OneLevelUpAction);
        Assert.That(((OneLevelUpAction)backKey.Action!).PageName, Is.EqualTo("parentPage"));
        Assert.That(backKey.Row, Is.EqualTo(Rows - 1));
        Assert.That(backKey.Column, Is.EqualTo(Cols - 1));

        // The folder page must declare its parent, otherwise the device enters it but the
        // return key does nothing (confirmed on real hardware).
        Assert.That(decoded.Pages[FolderPageIndex].ParentPageName, Is.EqualTo(decoded.Pages[HubPageIndex].PageName));
        Assert.That(decoded.Pages[HubPageIndex].ParentPageName, Is.Null, "ordinary pages must not carry parentPageName");
        Assert.That(decoded.Pages[RingAPageIndex].ParentPageName, Is.Null);

        string outPath = Path.Combine(Path.GetTempPath(), "mk20-navigation-theme.Theme");
        File.WriteAllBytes(outPath, encoded);
        TestContext.WriteLine($"Wrote {encoded.Length} bytes to {outPath}, {decoded.Assets.Count} asset(s)");
    }

    [Test]
    public void NavigationActions_CarryConfirmedRealMetadata()
    {
        // Field values confirmed by decoding the vendor's defaultTheme.Theme and a real
        // nested-folder theme - a builder-made navigation key must be indistinguishable.
        var jump = KeyActions.JumpToPage(2);
        Assert.Multiple(() =>
        {
            Assert.That(jump.RawType, Is.EqualTo("pageSwitch"));
            Assert.That(jump.PageSwitchMode, Is.EqualTo(0));
            Assert.That(jump.JumpToPage, Is.EqualTo(2));
            Assert.That(jump.ParentDescription, Is.EqualTo("Page switching"));
            Assert.That(jump.IconPath, Is.EqualTo("/static/icon/dark/PageSwitch.png"));
        });

        var open = KeyActions.OpenPage("00000000-0000-0000-0000-000000000001");
        Assert.Multiple(() =>
        {
            Assert.That(open.RawType, Is.EqualTo("openPage"));
            Assert.That(open.ParentDescription, Is.EqualTo("Page switching"));
            Assert.That(open.IconPath, Is.EqualTo("/static/icon/dark/createFolder.png"));
        });

        var up = KeyActions.OneLevelUp();
        Assert.Multiple(() =>
        {
            Assert.That(up.RawType, Is.EqualTo("oneLevelUp"));
            Assert.That(up.PageName, Is.EqualTo("parentPage"));
            Assert.That(up.ParentDescription, Is.EqualTo("Page switching"));
            Assert.That(up.IconPath, Is.EqualTo("/static/icon/dark/oneLevelUp.png"));
        });
    }

    [Test]
    public void JumpToPageAndOpenPage_SurviveARoundTrip()
    {
        var decoded = ThemeFileCodec.Decode(BuildNavigationTheme());

        var reEncoded = ThemeFileCodec.Encode(decoded);
        var again = ThemeFileCodec.Decode(reEncoded);

        var jumps = again.Pages
            .SelectMany(p => p.Items.OfType<KeyItem>())
            .Select(k => k.Action).OfType<PageSwitchAction>()
            .Where(a => a.PageSwitchMode == 0)
            .Select(a => a.JumpToPage)
            .OrderBy(i => i)
            .ToList();

        Assert.That(jumps, Is.EqualTo(new[] { HubPageIndex, HubPageIndex, RingAPageIndex, RingBPageIndex }));
        Assert.That(again.Pages.SelectMany(p => p.Items.OfType<KeyItem>()).Count(k => k.Action is OpenPageAction), Is.EqualTo(1));
        Assert.That(again.Pages.SelectMany(p => p.Items.OfType<KeyItem>()).Count(k => k.Action is OneLevelUpAction), Is.EqualTo(1));
    }
}
