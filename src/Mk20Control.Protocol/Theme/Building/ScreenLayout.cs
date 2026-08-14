namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// A rectangle in canvas pixels, with its top-left corner at (<see cref="X"/>, <see cref="Y"/>).
/// </summary>
/// <param name="X">Left edge, in canvas pixels from the canvas's top-left corner.</param>
/// <param name="Y">Top edge, in canvas pixels from the canvas's top-left corner.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    /// <summary>The x coordinate just past the right edge (<see cref="X"/> + <see cref="Width"/>).</summary>
    public double Right => X + Width;

    /// <summary>The y coordinate just past the bottom edge (<see cref="Y"/> + <see cref="Height"/>).</summary>
    public double Bottom => Y + Height;

    /// <summary>The horizontal centre of this rectangle.</summary>
    public double CenterX => X + (Width / 2);

    /// <summary>The vertical centre of this rectangle.</summary>
    public double CenterY => Y + (Height / 2);

    /// <summary>
    /// The top-left position at which an item of the given size would sit centred inside this
    /// rectangle - the usual way to place a caption or gauge over a key cell without having to
    /// work the offset out by hand.
    /// </summary>
    public (double X, double Y) CenterFor(double width, double height) =>
        (CenterX - (width / 2), CenterY - (height / 2));

    /// <summary>True if the given point falls inside this rectangle.</summary>
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>
/// The MK20's fixed screen geometry, in the canvas pixel coordinates that every theme item
/// is positioned in. All values are measured from the canvas's TOP-LEFT corner.
///
/// The 640x656 canvas covers two physically separate displays stacked vertically:
///
/// <code>
///   y=0    +---------------------------------------+
///          |        secondary screen strip         |  428x142 at x=106
///   y=144  +=======================================+
///          | r0c0 | r0c1 | r0c2 | r0c3 | r0c4      |
///          | r1c0 | r1c1 | r1c2 | r1c3 | r1c4      |  main screen:
///          | r2c0 | r2c1 | r2c2 | r2c3 | r2c4      |  5 columns x 4 rows
///          | r3c0 | r3c1 | r3c2 | r3c3 | r3c4      |  of 128x128 cells
///   y=656  +---------------------------------------+
/// </code>
///
/// The key grid is the main screen: cells are exactly <see cref="KeyCellSize"/> square and
/// tile it with no gaps, so cell (row, col) starts at (col*128, 144 + row*128). The physical
/// keys are visibly separated from one another, so artwork drawn across a cell boundary reads
/// as misaligned - use <see cref="KeyCell"/> and <see cref="LayoutRect.CenterFor"/> to place
/// things relative to a specific key rather than by eye.
/// </summary>
public static class ScreenLayout
{
    /// <summary>Full canvas width - the value to pass to <c>SetCanvas</c>.</summary>
    public const int CanvasWidth = 640;

    /// <summary>Full canvas height, covering both screens - the value to pass to <c>SetCanvas</c>.</summary>
    public const int CanvasHeight = 656;

    /// <summary>Number of key rows (top to bottom).</summary>
    public const int KeyRows = 4;

    /// <summary>Number of key columns (left to right).</summary>
    public const int KeyColumns = 5;

    /// <summary>Width and height of one key cell, in canvas pixels.</summary>
    public const int KeyCellSize = 128;

    /// <summary>The y coordinate at which the main screen (and therefore the key grid) begins.</summary>
    public const int MainScreenTop = 144;

    /// <summary>The secondary screen strip: 428x142 at (106, 0). Encoders sit at its far left and centre.</summary>
    public static LayoutRect SecondaryScreen { get; } = new(106, 0, 428, 142);

    /// <summary>The main screen - the whole key grid area: 640x512 at (0, 144).</summary>
    public static LayoutRect MainScreen { get; } =
        new(0, MainScreenTop, KeyColumns * KeyCellSize, KeyRows * KeyCellSize);

    /// <summary>
    /// The cell occupied by the key at (<paramref name="row"/>, <paramref name="column"/>),
    /// with <c>(0, 0)</c> being the top-left key.
    /// </summary>
    /// <param name="row">Key row, 0 (top) to 3 (bottom).</param>
    /// <param name="column">Key column, 0 (left) to 4 (right).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the row or column is outside the 4x5 grid.</exception>
    public static LayoutRect KeyCell(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, KeyRows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, KeyColumns);

        return new LayoutRect(
            column * KeyCellSize,
            MainScreenTop + (row * KeyCellSize),
            KeyCellSize,
            KeyCellSize);
    }

    /// <summary>
    /// The top-left corner of the key at (<paramref name="row"/>, <paramref name="column"/>) -
    /// the same position <c>ThemePageBuilder.AddKey(row, column, ...)</c> places the key at.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the row or column is outside the 4x5 grid.</exception>
    public static (double X, double Y) KeyTopLeft(int row, int column)
    {
        LayoutRect cell = KeyCell(row, column);
        return (cell.X, cell.Y);
    }

    /// <summary>
    /// Which key cell contains the given canvas point, or <c>null</c> if the point is not on
    /// the main screen (for example when it falls in the secondary strip above it).
    /// </summary>
    public static (int Row, int Column)? KeyAt(double x, double y)
    {
        if (!MainScreen.Contains(x, y))
        {
            return null;
        }

        return ((int)((y - MainScreenTop) / KeyCellSize), (int)(x / KeyCellSize));
    }

    /// <summary>Every key cell in the grid, in row-major order (r0c0, r0c1, ... r3c4).</summary>
    public static IEnumerable<(int Row, int Column, LayoutRect Cell)> AllKeyCells()
    {
        for (int row = 0; row < KeyRows; row++)
        {
            for (int column = 0; column < KeyColumns; column++)
            {
                yield return (row, column, KeyCell(row, column));
            }
        }
    }
}
