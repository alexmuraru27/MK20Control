using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and reloads/activates an already-present theme by its
/// device-side path via <c>SET_DEVICE_RELOAD</c>. Set the <c>MK20_THEME_PATH</c>
/// environment variable to the device-side path to reload (e.g.
/// <c>/data/theme/MK20/字母/字母.Theme</c>) - the test is skipped if it isn't set. Requires
/// <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>. Formerly
/// <c>Mk20Control.App</c> menu option 8.
/// </summary>
public class ReloadThemeTests
{
    [Test]
    public async Task ReloadTheme_AcknowledgesSuccessfully()
    {
        string? path = Environment.GetEnvironmentVariable("MK20_THEME_PATH");
        if (string.IsNullOrWhiteSpace(path))
            Assert.Ignore("Set the MK20_THEME_PATH environment variable to a device-side theme path to run this test.");

        await using var client = await HardwareConnection.OpenAsync();

        await client.ReloadThemeAsync(path!, TimeSpan.FromSeconds(20));

        TestContext.WriteLine($"Reload acknowledged for {path}. Visually confirm the device switched to this theme.");
    }
}
