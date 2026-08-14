using Mk20Control.Protocol.Theme.Building;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Pins <see cref="ScreenLayout"/> to the confirmed device geometry: a 640x656 canvas whose
/// top 142px are the secondary screen strip and whose lower 640x512 is a 5x4 grid of 128px
/// key cells starting at y=144. These are the same numbers <c>KeyItemBuilder</c> derives a
/// key's position from, so this also guards that builder against drift. No hardware required.
/// </summary>
public class ScreenLayoutTests
{
    [Test]
    public void Canvas_MatchesTheConfirmedGeometry()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScreenLayout.CanvasWidth, Is.EqualTo(640));
            Assert.That(ScreenLayout.CanvasHeight, Is.EqualTo(656));
            Assert.That(ScreenLayout.MainScreenTop, Is.EqualTo(144));

            // The grid must tile the main screen exactly, with no leftover pixels.
            Assert.That(ScreenLayout.KeyColumns * ScreenLayout.KeyCellSize, Is.EqualTo(ScreenLayout.CanvasWidth));
            Assert.That(ScreenLayout.MainScreen.Bottom, Is.EqualTo(ScreenLayout.CanvasHeight));
            Assert.That(ScreenLayout.MainScreen.Y, Is.EqualTo(ScreenLayout.SecondaryScreen.Bottom + 2),
                "the two screens are adjacent, separated only by the confirmed 2px seam");
        });
    }

    [Test]
    public void SecondaryScreen_IsThe428x142StripAt106_0()
    {
        LayoutRect strip = ScreenLayout.SecondaryScreen;

        Assert.Multiple(() =>
        {
            Assert.That((strip.X, strip.Y), Is.EqualTo((106d, 0d)));
            Assert.That((strip.Width, strip.Height), Is.EqualTo((428d, 142d)));
            Assert.That(strip.Right, Is.EqualTo(534));
        });
    }

    [TestCase(0, 0, 0, 144)]
    [TestCase(0, 4, 512, 144)]
    [TestCase(1, 2, 256, 272)]
    [TestCase(3, 4, 512, 528)]
    public void KeyTopLeft_MatchesColumnTimesCellPlusGridOrigin(int row, int column, double expectedX, double expectedY)
    {
        Assert.That(ScreenLayout.KeyTopLeft(row, column), Is.EqualTo((expectedX, expectedY)));
    }

    [Test]
    public void KeyTopLeft_MatchesWhatTheKeyBuilderActuallyEmits()
    {
        // The builder positions keys itself; if the two ever disagree, ScreenLayout would be
        // giving callers coordinates that do not line up with their own keys.
        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(ScreenLayout.CanvasWidth, ScreenLayout.CanvasHeight);
            foreach ((int row, int column, _) in ScreenLayout.AllKeyCells())
            {
                page.AddKey(row, column, key => key.Title($"r{row}c{column}"));
            }
        });

        var items = builder.Build().Pages[0].Items;
        int index = 0;
        foreach ((int row, int column, LayoutRect cell) in ScreenLayout.AllKeyCells())
        {
            var item = items[index++];
            Assert.That((item.X, item.Y), Is.EqualTo((cell.X, cell.Y)),
                $"key r{row}c{column} should sit at its ScreenLayout cell origin");
        }
    }

    [Test]
    public void CenterFor_PlacesAnItemOnTheCellCentre()
    {
        LayoutRect cell = ScreenLayout.KeyCell(1, 2);
        Assert.That(cell.CenterX, Is.EqualTo(320));

        // A 100px-wide dial centred on a 128px cell starts 14px in from its left edge.
        (double x, double y) = cell.CenterFor(100, 100);
        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(270));
            Assert.That(y, Is.EqualTo(cell.Y + 14));
        });
    }

    [Test]
    public void KeyAt_RoundTripsEveryCellAndRejectsTheSecondaryStrip()
    {
        foreach ((int row, int column, LayoutRect cell) in ScreenLayout.AllKeyCells())
        {
            Assert.That(ScreenLayout.KeyAt(cell.CenterX, cell.CenterY), Is.EqualTo((row, column)));
            Assert.That(ScreenLayout.KeyAt(cell.X, cell.Y), Is.EqualTo((row, column)),
                "the cell's own top-left corner belongs to that cell");
        }

        Assert.That(ScreenLayout.KeyAt(ScreenLayout.SecondaryScreen.CenterX, 70), Is.Null,
            "a point on the secondary screen is not a key");
    }

    [Test]
    public void AllKeyCells_YieldsTwentyCellsInRowMajorOrder()
    {
        var cells = ScreenLayout.AllKeyCells().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(cells, Has.Count.EqualTo(20));
            Assert.That((cells[0].Row, cells[0].Column), Is.EqualTo((0, 0)));
            Assert.That((cells[1].Row, cells[1].Column), Is.EqualTo((0, 1)));
            Assert.That((cells[5].Row, cells[5].Column), Is.EqualTo((1, 0)));
            Assert.That((cells[19].Row, cells[19].Column), Is.EqualTo((3, 4)));
        });
    }

    [TestCase(-1, 0)]
    [TestCase(4, 0)]
    [TestCase(0, -1)]
    [TestCase(0, 5)]
    public void KeyCell_RejectsPositionsOutsideTheGrid(int row, int column)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenLayout.KeyCell(row, column));
    }
}
