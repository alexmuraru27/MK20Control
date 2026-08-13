namespace Mk20Control.Protocol.Theme.Building;

/// <summary>
/// Well-known device-side paths for the vendor software's own built-in key icons, under
/// <c>/static/icon/&lt;theme&gt;/</c>. These are NOT theme assets: they already exist on the
/// device (and in the ScreenKeyWindows install), so a key can reference one via
/// <see cref="KeyItemBuilder.IconAssetPath"/> without embedding any image bytes - which is
/// exactly what real vendor themes do for navigation keys.
///
/// The <c>_128x128</c> variants are the key-sized artwork used as a KEY's icon; the plain
/// variants are the smaller category/action glyphs used as an ACTION's <c>iconPath</c>
/// (see <see cref="KeyActions"/>). Both spellings appear as key icons across real themes.
/// </summary>
public static class SystemIconPaths
{
    /// <summary>Key-sized page-switch arrows - used by previous/next and absolute-jump keys.</summary>
    public const string PageSwitch = "/static/icon/dark/PageSwitch_128x128.png";

    /// <summary>Key-sized "open folder" artwork - used by <see cref="KeyActions.OpenPage"/> keys.</summary>
    public const string CreateFolder = "/static/icon/dark/createFolder_128x128.png";

    /// <summary>Key-sized "return to previous level" arrow - used by <see cref="KeyActions.OneLevelUp"/> keys.</summary>
    public const string OneLevelUp = "/static/icon/dark/oneLevelUp_128x128.png";

    /// <summary>Small page-switch glyph, as carried in a page-switch action's <c>iconPath</c>.</summary>
    public const string PageSwitchGlyph = "/static/icon/dark/PageSwitch.png";

    /// <summary>Small folder glyph, as carried in an <c>openPage</c> action's <c>iconPath</c>.</summary>
    public const string CreateFolderGlyph = "/static/icon/dark/createFolder.png";

    /// <summary>Small return glyph, as carried in a <c>oneLevelUp</c> action's <c>iconPath</c>.</summary>
    public const string OneLevelUpGlyph = "/static/icon/dark/oneLevelUp.png";
}
