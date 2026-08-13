using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and deletes a theme by its device-side path via
/// <c>SET_DEVICE_DELETE_THEME</c>. Set the <c>MK20_THEME_PATH_TO_DELETE</c> environment
/// variable to the device-side path to delete - the test is skipped if it isn't set (this
/// is destructive, so it never runs by accident). Requires <c>MK20_COM_PORT</c> - see
/// <see cref="HardwareConnection"/>. Formerly <c>Mk20Control.App</c> menu option 12.
/// </summary>
public class DeleteThemeTests
{
    [Test]
    public async Task DeleteTheme_RemovesSuccessfully()
    {
        string? path = Environment.GetEnvironmentVariable("MK20_THEME_PATH_TO_DELETE");
        if (string.IsNullOrWhiteSpace(path))
            Assert.Ignore("Set the MK20_THEME_PATH_TO_DELETE environment variable to a device-side theme path to run this (destructive) test.");

        await using var client = await HardwareConnection.OpenAsync();

        await client.DeleteThemeAsync(path!);

        TestContext.WriteLine($"Theme deleted: {path}");
    }
}
