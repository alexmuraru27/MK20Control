namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Which of the MK20's two physical rotary encoders a key is bound to.
///
/// An encoder is not a distinct item type: it is an ordinary key positioned at a fixed
/// secondary-screen coordinate, which is how the device recognises it. Those coordinates are
/// confirmed real values, cross-checked against <c>defaultTheme.Theme</c> and
/// <c>海边吹风.Theme</c>.
/// </summary>
public enum EncoderSide
{
    /// <summary>The left-hand rotary encoder - always positioned at x=106, y=0.</summary>
    Left,

    /// <summary>The right-hand rotary encoder - always positioned at x=320, y=0.</summary>
    Right,
}

/// <summary>
/// The fixed secondary-screen coordinates and the device's reported pseudo-row for each
/// physical rotary encoder.
///
/// <b>How encoder input is reported.</b> Turning or clicking an encoder produces exactly the
/// same <c>DEVICE_ProactiveEscalationCMD</c> event as a button press, except that
/// <c>row</c> and <c>col</c> both carry a pseudo-row identifying the encoder, <c>pressed</c>
/// is always 1 (there is no release), and <c>map[1]</c> echoes the encoder key's action -
/// which is what lets a <c>KeyActions.Command(id)</c> on an encoder reach your own C#.
///
/// <b>Direction is NOT reported.</b> Confirmed on real hardware: rotating clockwise,
/// rotating counter-clockwise and clicking all produce the identical pseudo-row
/// (<see cref="LeftPseudoRow"/> / <see cref="RightPseudoRow"/>) with no distinguishing
/// field. A command-bound encoder therefore tells you "this knob was actuated", not which
/// way it moved. For direction-sensitive behaviour use
/// <c>KeyActions.EncoderKeyboard(...)</c>, which binds a separate keystroke to rotate-left,
/// click and rotate-right and is executed natively by the device.
/// </summary>
public static class EncoderPositions
{
    /// <summary>Confirmed real position of the LEFT encoder key.</summary>
    public const double LeftX = 106, LeftY = 0;

    /// <summary>Confirmed real position of the RIGHT encoder key.</summary>
    public const double RightX = 320, RightY = 0;

    /// <summary>The row/col value the device reports for any LEFT encoder activity (turn or click). Confirmed on hardware.</summary>
    public const int LeftPseudoRow = 100;

    /// <summary>The row/col value the device reports for any RIGHT encoder activity (turn or click). Confirmed on hardware.</summary>
    public const int RightPseudoRow = 103;


    /// <summary>
    /// Builds the <c>relatedTheme</c> path a vendor encoder action carries - the mini-theme
    /// shown on the encoder's own small display while that function is active.
    ///
    /// ScreenKeyWindows writes an ABSOLUTE path into its local install, e.g.
    /// <c>C:/Users/&lt;you&gt;/.../ScreenKeyWindows_v1_1/theme/MK20/Encoder/relatedTheme/system_volume.Theme</c>
    /// (confirmed by having the vendor app re-save a theme built by this library). Real themes
    /// downloaded from elsewhere carry the ORIGINAL author's path - one examined still points
    /// at a <c>MK20-PLUS</c> folder on a different machine - so the device evidently tolerates
    /// a path that does not resolve locally.
    /// </summary>
    /// <param name="screenKeyWindowsRoot">The ScreenKeyWindows install folder (the one containing <c>theme</c>).</param>
    /// <param name="type">Which built-in function the encoder is bound to.</param>
    public static string RelatedThemePath(string screenKeyWindowsRoot, EncoderFunctionType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenKeyWindowsRoot);
        string fileName = type switch
        {
            EncoderFunctionType.SystemVolume => "system_volume",
            EncoderFunctionType.DeviceBrightness => "device_brightness",
            EncoderFunctionType.SystemMedia => "system_media",
            EncoderFunctionType.DeviceVolume => "device_volume",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown EncoderFunctionType."),
        };
        return $"{screenKeyWindowsRoot.Replace('\\', '/').TrimEnd('/')}/theme/MK20/Encoder/relatedTheme/{fileName}.Theme";
    }

    /// <summary>The x/y coordinate for the given encoder.</summary>
    public static (double X, double Y) PositionOf(EncoderSide side) =>
        side == EncoderSide.Left ? (LeftX, LeftY) : (RightX, RightY);

    /// <summary>The pseudo-row the device reports for the given encoder.</summary>
    public static int PseudoRowOf(EncoderSide side) =>
        side == EncoderSide.Left ? LeftPseudoRow : RightPseudoRow;

    /// <summary>Which encoder a reported pseudo-row belongs to, or null if the row is an ordinary key.</summary>
    public static EncoderSide? SideOfPseudoRow(int row) => row switch
    {
        LeftPseudoRow => EncoderSide.Left,
        RightPseudoRow => EncoderSide.Right,
        _ => null,
    };
}
