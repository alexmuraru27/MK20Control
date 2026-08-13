using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and requests its identity via <c>FIND_DEVICE</c> (version,
/// screen model/size, volume, backlight, name). Requires <c>MK20_COM_PORT</c> - see
/// <see cref="HardwareConnection"/>. Formerly <c>Mk20Control.App</c> menu option 4.
/// </summary>
public class PingDeviceTests
{
    [Test]
    public async Task Ping_ReturnsDeviceIdentity()
    {
        await using var client = await HardwareConnection.OpenAsync();

        var identity = await client.TryPingAsync();

        Assert.That(identity, Is.Not.Null, "No identity announcement observed within the timeout.");
        TestContext.WriteLine($"version={identity!.Version} screen={identity.ScreenModel} " +
            $"{identity.ScreenWidth}x{identity.ScreenHeight} volume={identity.DeviceVolume} " +
            $"backlight={identity.DeviceBacklight} name={identity.DeviceName}");
    }
}
