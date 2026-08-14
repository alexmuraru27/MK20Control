using Mk20Control.IntegrationTests.Support;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Builds a theme whose buttons carry caller-defined COMMAND IDs rather than keystrokes or
/// text - the shape an application uses when it wants full control of what each button does.
///
/// Deliberately spreads the SAME grid cell across two pages plus a folder, because that is the
/// case grid-position binding cannot handle: the device's press event reports only
/// <c>{row, col, pressed}</c> and never says which page it came from, so r0c0 on page 1, r0c0
/// on page 2 and r0c0 in the folder are indistinguishable by position - and are told apart
/// purely by their ids.
/// </summary>
public class CommandThemeBuilderTests
{
    public const int Rows = 4, Cols = 5;

    /// <summary>Every command id this theme binds, with the cell it sits in.</summary>
    public static readonly (string Id, int Page, int Row, int Col)[] Commands =
    {
        // Page 0 - all three of these share cells with buttons on other pages.
        ("pit.request",  0, 0, 0),
        ("pit.cancel",   0, 0, 1),
        ("tc.up",        0, 1, 0),

        // Page 1 - note r0c0/r0c1 collide with page 0 by position.
        ("fuel.plus",    1, 0, 0),
        ("fuel.minus",   1, 0, 1),
        ("abs.up",       1, 1, 0),

        // Folder page - r0c0 again.
        ("wipers.cycle", 2, 0, 0),
        ("lights.flash", 2, 0, 1),
    };

    public static byte[] BuildCommandTheme()
    {
        string iconPath = TestPaths.IconFile(1);
        Assert.That(File.Exists(iconPath), Is.True, $"Missing icon file: {iconPath}. Run tools\\AssetGenerator first.");
        byte[] iconBytes = File.ReadAllBytes(iconPath);

        var builder = new ThemeBuilder();
        var home = builder.AddPage().SetCanvas(640, 656);
        var second = builder.AddPage().SetCanvas(640, 656);
        var folder = builder.AddPage().SetCanvas(640, 656).AsFolderOf(home);

        var pages = new[] { home, second, folder };

        foreach (var (id, pageIndex, row, col) in Commands)
        {
            pages[pageIndex].AddKey(row, col, key => key
                .Icon("icon_01.png", iconBytes)
                .Title(id)
                .Action(KeyActions.Command(id)));
        }

        // Navigation, executed by the device itself - these carry no command id.
        home.AddKey(Rows - 1, 0, key => key
            .IconDevice(DeviceIcon.PageSwitch).Title("PREV").Action(KeyActions.PreviousPage()));
        home.AddKey(Rows - 1, Cols - 1, key => key
            .IconDevice(DeviceIcon.PageSwitch).Title("NEXT").Action(KeyActions.NextPage()));
        home.AddKey(Rows - 1, 2, key => key
            .IconDevice(DeviceIcon.OpenFolder).Title("FOLDER").Action(KeyActions.OpenPage(folder.PageId)));

        second.AddKey(Rows - 1, 0, key => key
            .IconDevice(DeviceIcon.PageSwitch).Title("PREV").Action(KeyActions.PreviousPage()));
        second.AddKey(Rows - 1, Cols - 1, key => key
            .IconDevice(DeviceIcon.PageSwitch).Title("NEXT").Action(KeyActions.NextPage()));

        folder.AddKey(Rows - 1, Cols - 1, key => key
            .IconDevice(DeviceIcon.OneLevelUp).Title("BACK").Action(KeyActions.OneLevelUp()));

        return ThemeFileCodec.Encode(builder.Build());
    }

    [Test]
    public void EveryCommandId_SurvivesTheRoundTrip()
    {
        var decoded = ThemeFileCodec.Decode(BuildCommandTheme());

        var ids = decoded.Pages
            .SelectMany(p => p.Items.OfType<KeyItem>())
            .Select(k => k.Action).OfType<TextInputAction>()
            .Select(a => a.InputText)
            .ToList();

        Assert.That(ids, Is.EquivalentTo(Commands.Select(c => c.Id)));
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), "command ids must be unique");
    }

    [Test]
    public void TheSameCellOnDifferentPages_CarriesDifferentIds()
    {
        var decoded = ThemeFileCodec.Decode(BuildCommandTheme());

        // r0c0 exists on all three pages - by position they are identical, which is exactly
        // why a binding keyed on position would be ambiguous.
        var atR0C0 = decoded.Pages
            .SelectMany(p => p.Items.OfType<KeyItem>())
            .Where(k => k.Row == 0 && k.Column == 0)
            .Select(k => (k.Action as TextInputAction)?.InputText)
            .ToList();

        Assert.That(atR0C0, Has.Count.EqualTo(3));
        Assert.That(atR0C0, Is.EquivalentTo(new[] { "pit.request", "fuel.plus", "wipers.cycle" }));
    }

    [Test]
    public void NavigationKeys_CarryNoCommandId()
    {
        var decoded = ThemeFileCodec.Decode(BuildCommandTheme());

        var navigation = decoded.Pages
            .SelectMany(p => p.Items.OfType<KeyItem>())
            .Where(k => k.Action is PageSwitchAction or OpenPageAction or OneLevelUpAction)
            .ToList();

        Assert.That(navigation, Has.Count.EqualTo(6));
        Assert.That(navigation.All(k => k.Action is not TextInputAction), Is.True,
            "navigation is executed by the device and must not look like a command");
    }

    [Test]
    public void FolderPage_DeclaresItsParent()
    {
        var decoded = ThemeFileCodec.Decode(BuildCommandTheme());

        Assert.That(decoded.Pages[2].ParentPageName, Is.EqualTo(decoded.Pages[0].PageName));
        Assert.That(decoded.Pages[0].ParentPageName, Is.Null);
        Assert.That(decoded.Pages[1].ParentPageName, Is.Null);
    }
}
