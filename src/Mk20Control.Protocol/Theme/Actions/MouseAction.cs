namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// A key assigned to a mouse action ("type": "qmk_mouse") - click, movement, or scroll.
/// The exact enumeration of <see cref="MouseKey"/>/<see cref="MouseEvent"/> values has not
/// been individually confirmed for every possible option; the raw integers observed are
/// exposed as-is rather than mapped to a guessed enum.
/// </summary>
public sealed record MouseAction : KeyAction
{
    public int MouseKey { get; init; }
    public int MouseEvent { get; init; }
    public int MouseX { get; init; }
    public int MouseY { get; init; }

    /// <summary>Vertical scroll delta ("mouse_v").</summary>
    public int MouseVerticalScroll { get; init; }

    /// <summary>Horizontal scroll delta ("mouse_h").</summary>
    public int MouseHorizontalScroll { get; init; }
}
