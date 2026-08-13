using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and sets its backlight level via <c>SET_DEVICE_BL</c>. Set the
/// <c>MK20_BACKLIGHT_LEVEL</c> environment variable (0-100) to override the default of 80.
/// Requires <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>. Formerly
/// <c>Mk20Control.App</c> menu option 5.
/// </summary>
public class SetBacklightTests
{
    [Test]
    public async Task SetBacklight_SendsSuccessfully()
    {
        await using var client = await HardwareConnection.OpenAsync();

        int level = int.TryParse(Environment.GetEnvironmentVariable("MK20_BACKLIGHT_LEVEL"), out int v) ? v : 80;
        await client.SetBacklightAsync(level);

        TestContext.WriteLine($"Backlight set to {level}. Visually confirm the device's brightness changed.");
    }
}
