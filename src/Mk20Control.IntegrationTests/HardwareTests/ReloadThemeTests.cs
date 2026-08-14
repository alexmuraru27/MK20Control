using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and reloads/activates an already-present theme by name via
/// <c>SET_DEVICE_RELOAD</c>. Set the <c>MK20_THEME_NAME</c> environment variable to the theme
/// to reload (e.g. <c>字母</c>) - the test is skipped if it isn't set. Requires
/// <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>. Formerly
/// <c>Mk20Control.App</c> menu option 8.
/// </summary>
public class ReloadThemeTests
{
    [Test]
    public async Task ReloadTheme_AcknowledgesSuccessfully()
    {
        string? themeName = Environment.GetEnvironmentVariable("MK20_THEME_NAME");
        if (string.IsNullOrWhiteSpace(themeName))
            Assert.Ignore("Set the MK20_THEME_NAME environment variable to an installed theme's name to run this test.");

        await using var client = await HardwareConnection.OpenAsync();

        await client.ReloadThemeAsync(themeName!, TimeSpan.FromSeconds(20));

        TestContext.WriteLine($"Reload acknowledged for '{themeName}'. Visually confirm the device switched to this theme.");
    }
}
