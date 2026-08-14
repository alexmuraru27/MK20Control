namespace Mk20Control.IntegrationTests.Support;

/// <summary>
/// Fixed, well-known theme NAMES for every hardware test scenario in this project - each test
/// uploads under the SAME name every run by default, so re-running a test overwrites its own
/// previous theme instead of leaving an ever-growing pile of differently-named theme files
/// (each carrying its own embedded copy of every icon/GIF/background it uses) on the device's
/// SD card.
///
/// These are ordinary theme names, stored exactly where any other theme goes - the client
/// resolves them through <c>DeviceThemePath</c> like it does for application themes. They are
/// prefixed so they are easy to spot and remove on a device you also use normally.
///
/// Every uploaded <c>.Theme</c> file is fully self-contained: deleting it via
/// <c>Mk20DeviceClient.DeleteThemeAsync</c> (SET_DEVICE_DELETE_THEME) removes 100% of its
/// data in one operation, since all of its assets are embedded inside that single file -
/// there is no separate/shared on-device asset store to clean up (see
/// PROTOCOL_WAVESHARE_MK20.md §6.6/§7). The only way SD card space actually accumulates is
/// by uploading under a NEW name each run instead of reusing the same one, which is exactly
/// what these fixed defaults prevent.
///
/// Every hardware upload test still honors the <c>MK20_UPLOAD_THEME_NAME</c> environment
/// variable as an explicit override, for callers who want a custom/parallel theme (e.g. to
/// compare two versions side by side) - but no longer *requires* it.
/// </summary>
public static class DeviceThemeNames
{
    private const string Prefix = "mk20control-test-";

    public static readonly string FiveKeyTest = $"{Prefix}test5";
    public static readonly string FullGrid = $"{Prefix}fullgrid";
    public static readonly string EncoderVolumeAndBrightness = $"{Prefix}encoders";
    public static readonly string SecondaryScreenGaugesOverlay = $"{Prefix}gaugesbox";
    public static readonly string MainScreenAllWidgetTypes = $"{Prefix}widgettest";
    public static readonly string ThemeEditorAddKey = $"{Prefix}edited";
    public static readonly string TextMacros = $"{Prefix}textmacros";
    public static readonly string Commands = $"{Prefix}commands";
    public static readonly string EncoderCommands = $"{Prefix}enccmd";
    public static readonly string Showcase = $"{Prefix}showcase";
    public static readonly string Navigation = $"{Prefix}navigation";

    /// <summary>Returns the <c>MK20_UPLOAD_THEME_NAME</c> environment variable's value if set, otherwise <paramref name="defaultThemeName"/>.</summary>
    public static string Resolve(string defaultThemeName)
    {
        string? overrideName = Environment.GetEnvironmentVariable("MK20_UPLOAD_THEME_NAME");
        return string.IsNullOrWhiteSpace(overrideName) ? defaultThemeName : overrideName;
    }
}
