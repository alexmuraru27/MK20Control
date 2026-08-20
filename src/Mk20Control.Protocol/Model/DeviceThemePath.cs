using System;
using System.Linq;

namespace Mk20Control.Protocol.Model;

/// <summary>
/// Builds the device-side path a theme lives at, so callers never have to write one by hand.
///
/// The device stores every theme under a fixed, confirmed layout (see
/// PROTOCOL_WAVESHARE_MK20.md §5.2/§6.6): a directory named after the theme, containing a
/// single <c>.Theme</c> file of the same name:
///
///   <c>/data/theme/MK20/&lt;name&gt;/&lt;name&gt;.Theme</c>
///
/// The name is the only free part, which is why the client's theme operations take a name
/// rather than a path: a caller cannot accidentally write outside the theme directory, use
/// the wrong extension, or mismatch the directory and file name (the device does not list a
/// theme whose file name differs from its folder).
/// </summary>
public static class DeviceThemePath
{
    /// <summary>The directory every theme folder is created in.</summary>
    public const string Root = "/data/theme/MK20";

    /// <summary>The extension the device expects, including the dot. Case is significant.</summary>
    public const string Extension = ".Theme";

    /// <summary>Longest accepted theme name. Generous - real names are short - but bounded.</summary>
    public const int MaxThemeNameLength = 64;

    /// <summary>
    /// Returns the device-side path for <paramref name="themeName"/>, e.g. "example-monitor"
    /// becomes "/data/theme/MK20/example-monitor/example-monitor.Theme".
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown if the name is empty, too long, or contains anything that could make it resolve
    /// somewhere other than its own theme folder - see <see cref="IsValidThemeName"/>.
    /// </exception>
    public static string ForTheme(string themeName)
    {
        if (!IsValidThemeName(themeName, out string? problem))
        {
            throw new ArgumentException(
                $"'{themeName}' is not a usable theme name: {problem} A theme name is a single " +
                $"folder name - the client turns it into {Root}/<name>/<name>{Extension}.",
                nameof(themeName));
        }

        return $"{Root}/{themeName}/{themeName}{Extension}";
    }

    /// <summary>
    /// True if <paramref name="themeName"/> is a name this library will build a path from.
    /// Non-ASCII names are fine (the vendor's own themes use Chinese names); what is rejected
    /// is anything that is not a plain single folder name.
    /// </summary>
    public static bool IsValidThemeName(string? themeName) => IsValidThemeName(themeName, out _);

    /// <summary>
    /// The inverse of <see cref="ForTheme"/>: recovers the theme name from a device-side path
    /// such as the ones <see cref="ThemeListing"/> reports. Returns false for any path that
    /// does not follow the standard layout - the device does also hold themes elsewhere (for
    /// example the secondary-screen ones under a nested folder), so a false result means
    /// "not addressable by name", not "invalid".
    /// </summary>
    public static bool TryGetThemeName(string? deviceThemePath, out string themeName)
    {
        themeName = string.Empty;

        if (string.IsNullOrWhiteSpace(deviceThemePath))
        {
            return false;
        }

        string prefix = Root + "/";
        if (!deviceThemePath!.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = deviceThemePath[prefix.Length..].Split('/');
        if (parts.Length != 2 || parts[1] != parts[0] + Extension || !IsValidThemeName(parts[0]))
        {
            return false;
        }

        themeName = parts[0];
        return true;
    }

    private static bool IsValidThemeName(string? themeName, out string? problem)
    {
        if (string.IsNullOrWhiteSpace(themeName))
        {
            problem = "it is empty.";
            return false;
        }

        if (themeName!.Length > MaxThemeNameLength)
        {
            problem = $"it is longer than {MaxThemeNameLength} characters.";
            return false;
        }

        if (themeName.Contains('/') || themeName.Contains('\\'))
        {
            problem = "it contains a path separator, so it would not be a single folder.";
            return false;
        }

        // "." and ".." resolve outside the theme's own folder; no real theme name needs a
        // run of dots, so reject them outright rather than reasoning about where they sit.
        if (themeName.Contains("..", StringComparison.Ordinal) || themeName.All(character => character == '.'))
        {
            problem = "it contains \"..\" or consists only of dots, which would not stay inside its own folder.";
            return false;
        }

        if (themeName != themeName.Trim())
        {
            problem = "it starts or ends with whitespace.";
            return false;
        }

        if (themeName.Any(char.IsControl))
        {
            problem = "it contains a control character.";
            return false;
        }

        problem = null;
        return true;
    }
}
