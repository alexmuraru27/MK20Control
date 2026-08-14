using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and deletes a theme by name via
/// <c>SET_DEVICE_DELETE_THEME</c>. Set the <c>MK20_THEME_NAME_TO_DELETE</c> environment
/// variable to the theme to delete - the test is skipped if it isn't set (this
/// is destructive, so it never runs by accident). Requires <c>MK20_COM_PORT</c> - see
/// <see cref="HardwareConnection"/>. Formerly <c>Mk20Control.App</c> menu option 12.
/// </summary>
public class DeleteThemeTests
{
    [Test]
    public async Task DeleteTheme_RemovesSuccessfully()
    {
        string? themeName = Environment.GetEnvironmentVariable("MK20_THEME_NAME_TO_DELETE");
        if (string.IsNullOrWhiteSpace(themeName))
            Assert.Ignore("Set the MK20_THEME_NAME_TO_DELETE environment variable to an installed theme's name to run this (destructive) test.");

        await using var client = await HardwareConnection.OpenAsync();

        await client.DeleteThemeAsync(themeName!);

        TestContext.WriteLine($"Theme deleted: '{themeName}'");
    }
}
