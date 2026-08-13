using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and uploads an arbitrary local <c>.Theme</c> file, activating
/// it (<c>FILE_START</c> + bulk transfer + <c>FILE_END</c> + <c>SET_DEVICE_RELOAD</c>). Set
/// <c>MK20_UPLOAD_LOCAL_PATH</c> to the local file and <c>MK20_UPLOAD_DEVICE_PATH</c> to the
/// destination device-side path - the test is skipped if either isn't set. Requires
/// <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>. Formerly
/// <c>Mk20Control.App</c> menu option 13.
/// </summary>
public class UploadThemeFileTests
{
    [Test]
    public async Task UploadThemeFile_ActivatesSuccessfully()
    {
        string? localPath = Environment.GetEnvironmentVariable("MK20_UPLOAD_LOCAL_PATH");
        string? devicePath = Environment.GetEnvironmentVariable("MK20_UPLOAD_DEVICE_PATH");
        if (string.IsNullOrWhiteSpace(localPath) || string.IsNullOrWhiteSpace(devicePath))
            Assert.Ignore("Set MK20_UPLOAD_LOCAL_PATH and MK20_UPLOAD_DEVICE_PATH to run this test.");
        if (!File.Exists(localPath))
            Assert.Fail($"File not found: {localPath}");

        await using var client = await HardwareConnection.OpenAsync();

        byte[] bytes = File.ReadAllBytes(localPath!);
        TestContext.WriteLine($"Uploading {bytes.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath!, bytes, TimeSpan.FromSeconds(30));

        TestContext.WriteLine("Upload complete and theme activated.");
    }
}
