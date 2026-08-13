using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and lists its installed themes plus free storage space via
/// <c>GET_DEVICE_THEME</c>. Requires <c>MK20_COM_PORT</c> - see
/// <see cref="HardwareConnection"/>. Formerly <c>Mk20Control.App</c> menu option 7.
/// </summary>
public class GetInstalledThemesTests
{
    [Test]
    public async Task GetInstalledThemes_ListsThemesAndFreeSpace()
    {
        await using var client = await HardwareConnection.OpenAsync();

        var listing = await client.GetInstalledThemesAsync();

        TestContext.WriteLine($"Free space: {listing.BytesAvailable}/{listing.BytesTotal} bytes");
        foreach (var theme in listing.Themes)
            TestContext.WriteLine($"  {theme.Path}  (crc32=0x{theme.Crc32:x8})");

        Assert.That(listing.Themes, Is.Not.Null);
    }
}
