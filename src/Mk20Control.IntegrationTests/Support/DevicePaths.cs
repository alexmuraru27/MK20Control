namespace Mk20Control.IntegrationTests.Support;

/// <summary>
/// Fixed, well-known device-side theme paths for every hardware test scenario in this
/// project - each test uploads to the SAME path every run by default, so re-running a test
/// overwrites its own previous theme instead of leaving an ever-growing pile of
/// differently-named theme files (each carrying its own embedded copy of every icon/GIF/
/// background it uses) on the device's SD card.
///
/// Every uploaded <c>.Theme</c> file is fully self-contained: deleting it via
/// <c>Mk20DeviceClient.DeleteThemeAsync</c> (SET_DEVICE_DELETE_THEME) removes 100% of its
/// data in one operation, since all of its assets are embedded inside that single file -
/// there is no separate/shared on-device asset store to clean up (see
/// PROTOCOL_WAVESHARE_MK20.md §6.6/§7). The only way SD card space actually accumulates is
/// by uploading under a NEW path each run instead of reusing the same one, which is exactly
/// what these fixed defaults prevent.
///
/// Every hardware upload test still honors the <c>MK20_UPLOAD_DEVICE_PATH</c> environment
/// variable as an explicit override, for callers who want a custom/parallel path (e.g. to
/// compare two versions side by side) - but no longer *requires* it.
/// </summary>
public static class DevicePaths
{
    private const string Root = "/data/theme/MK20/mk20control-tests";

    public static readonly string FiveKeyTest = $"{Root}/test5/test5.Theme";
    public static readonly string FullGrid = $"{Root}/fullgrid/fullgrid.Theme";
    public static readonly string EncoderVolumeAndBrightness = $"{Root}/encoders/encoders.Theme";
    public static readonly string SecondaryScreenGaugesOverlay = $"{Root}/gaugesbox/gaugesbox.Theme";
    public static readonly string MainScreenAllWidgetTypes = $"{Root}/widgettest/widgettest.Theme";
    public static readonly string ThemeEditorAddKey = $"{Root}/edited/edited.Theme";
    public static readonly string Navigation = $"{Root}/navigation/navigation.Theme";

    /// <summary>Returns the <c>MK20_UPLOAD_DEVICE_PATH</c> environment variable's value if set, otherwise <paramref name="defaultPath"/>.</summary>
    public static string Resolve(string defaultPath)
    {
        string? overridePath = Environment.GetEnvironmentVariable("MK20_UPLOAD_DEVICE_PATH");
        return string.IsNullOrWhiteSpace(overridePath) ? defaultPath : overridePath;
    }
}
