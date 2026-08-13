using Mk20Control.IntegrationTests.Support;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Proves the builder can reproduce an arbitrarily deep chain of nested "folders", matching
/// the shape of a real vendor-created nested-folder theme (a <c>ctrlaltdel.Theme</c> edited in
/// ScreenKeyWindows was found nested five levels deep):
///
/// <code>
/// page 0 (root) -openPage-> page 1 -openPage-> page 2 -openPage-> ... -> page N (leaf)
///                              |                  |                        |
///                              +-- oneLevelUp -----+------------------------+  (all "parentPage")
/// </code>
///
/// The key confirmed detail this locks in: <b>nesting depth is not expressed in the file at
/// all</b>. Every page sits in the same flat <c>pages</c> array, a "folder" is just a page some
/// key opens by GUID, and every return key - at any depth - emits the identical sentinel
/// <c>pageName="parentPage"</c> rather than naming its parent. The device therefore pops a
/// runtime navigation stack, which is also why the same folder page can be opened from
/// several places and still return to whichever one opened it.
///
/// No hardware required.
/// </summary>
public class NestedFolderThemeBuilderTests
{
    public const int Rows = 4, Cols = 5;

    /// <summary>Matches the depth of the real vendor nested-folder theme this test mirrors.</summary>
    public const int DefaultDepth = 5;

    /// <summary>
    /// Builds a root page plus <paramref name="depth"/> nested folder pages. Each level holds
    /// one keyboard key (so the level is visibly distinct on-device), a key opening the next
    /// level, and - on every level below the root - a return key at the confirmed bottom-right
    /// cell.
    /// </summary>
    public static byte[] BuildNestedFolderTheme(int depth = DefaultDepth)
    {
        Assert.That(depth, Is.GreaterThan(0));

        string iconPath = TestPaths.IconFile(1);
        Assert.That(File.Exists(iconPath), Is.True, $"Missing icon file: {iconPath}. Run tools\\AssetGenerator first.");
        byte[] iconBytes = File.ReadAllBytes(iconPath);

        var builder = new ThemeBuilder();

        // Create every page first: a key can only open a page whose PageId already exists, and
        // the chain is wired from the deepest level back up.
        var pages = new List<ThemePageBuilder>();
        for (int level = 0; level <= depth; level++)
        {
            pages.Add(builder.AddPage().SetCanvas(640, 656));
        }

        // Every level below the root is a folder whose parent is the level above it - this
        // page-level link is what a oneLevelUp key actually returns along.
        for (int level = 1; level <= depth; level++)
        {
            pages[level].AsFolderOf(pages[level - 1]);
        }

        for (int level = 0; level <= depth; level++)
        {
            ThemePageBuilder page = pages[level];

            page.AddKey(0, 0, key => key
                .Icon("icon_01.png", iconBytes)
                .Title($"LEVEL {level}")
                .Action(KeyActions.Keyboard(HidKey.A, "A")));

            if (level < depth)
            {
                ThemePageBuilder child = pages[level + 1];
                page.AddKey(0, 1, key => key
                    .IconAssetPath(SystemIconPaths.CreateFolder)
                    .Title("OPEN")
                    .Action(KeyActions.OpenPage(child.PageId)));
            }

            if (level > 0)
            {
                page.AddKey(Rows - 1, Cols - 1, key => key
                    .IconAssetPath(SystemIconPaths.OneLevelUp)
                    .Title("BACK")
                    .Action(KeyActions.OneLevelUp()));
            }
        }

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void BuildNestedFolderTheme_ChainsToTheRequestedDepth()
    {
        byte[] encoded = BuildNestedFolderTheme();
        var decoded = ThemeFileCodec.Decode(encoded);

        Assert.That(decoded.Pages, Has.Count.EqualTo(DefaultDepth + 1));

        // Walk the chain from the root by GUID, exactly as the device would.
        var pageById = decoded.Pages
            .Where(p => p.PageName is not null)
            .ToDictionary(p => p.PageName!, p => p);

        ThemePage current = decoded.Pages[0];
        int visited = 0;
        while (true)
        {
            var open = current.Items.OfType<KeyItem>()
                .Select(k => k.Action).OfType<OpenPageAction>().SingleOrDefault();
            if (open is null) break;

            Assert.That(pageById.ContainsKey(open.PageName), Is.True, $"level {visited} opens an unknown page");
            current = pageById[open.PageName];
            visited++;
        }

        Assert.That(visited, Is.EqualTo(DefaultDepth), "the chain should be reachable end to end from the root");

        // Each level must declare the level above it as its parent; the root must not.
        Assert.That(decoded.Pages[0].ParentPageName, Is.Null);
        for (int level = 1; level < decoded.Pages.Count; level++)
        {
            Assert.That(
                decoded.Pages[level].ParentPageName,
                Is.EqualTo(decoded.Pages[level - 1].PageName),
                $"level {level} must be a folder of level {level - 1}");
        }

        // The leaf is the only page with no folder-open key, and it still returns.
        Assert.That(current.Items.OfType<KeyItem>().Any(k => k.Action is OneLevelUpAction), Is.True);
    }

    [Test]
    public void EveryReturnKey_UsesTheParentPageSentinelAtTheBottomRight()
    {
        var decoded = ThemeFileCodec.Decode(BuildNestedFolderTheme());

        var returnKeys = decoded.Pages
            .SelectMany(p => p.Items.OfType<KeyItem>())
            .Where(k => k.Action is OneLevelUpAction)
            .ToList();

        // One per level below the root - the root itself has nothing to return to.
        Assert.That(returnKeys, Has.Count.EqualTo(DefaultDepth));
        Assert.That(decoded.Pages[0].Items.OfType<KeyItem>().Any(k => k.Action is OneLevelUpAction), Is.False);

        foreach (var key in returnKeys)
        {
            var action = (OneLevelUpAction)key.Action!;
            Assert.Multiple(() =>
            {
                // Confirmed: depth is never encoded - every level emits the same sentinel.
                Assert.That(action.PageName, Is.EqualTo("parentPage"));
                Assert.That(key.Row, Is.EqualTo(Rows - 1));
                Assert.That(key.Column, Is.EqualTo(Cols - 1));
            });
        }
    }

    [Test]
    public void NestedFolders_WorkAtAnyDepth()
    {
        foreach (int depth in new[] { 1, 3, 8 })
        {
            var decoded = ThemeFileCodec.Decode(BuildNestedFolderTheme(depth));
            var keys = decoded.Pages.SelectMany(p => p.Items.OfType<KeyItem>()).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(decoded.Pages, Has.Count.EqualTo(depth + 1), $"depth {depth} pages");
                Assert.That(keys.Count(k => k.Action is OpenPageAction), Is.EqualTo(depth), $"depth {depth} open keys");
                Assert.That(keys.Count(k => k.Action is OneLevelUpAction), Is.EqualTo(depth), $"depth {depth} return keys");
            });
        }
    }
}
