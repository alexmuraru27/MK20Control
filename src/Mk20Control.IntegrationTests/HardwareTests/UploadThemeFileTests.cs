using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Connects to a real device and uploads an arbitrary local <c>.Theme</c> file, activating
/// it (<c>FILE_START</c> + bulk transfer + <c>FILE_END</c> + <c>SET_DEVICE_RELOAD</c>). Set
/// <c>MK20_UPLOAD_LOCAL_PATH</c> to the local file - the test is skipped if it isn't set
/// (there's no self-contained default here since the whole point is uploading an arbitrary
/// caller-supplied file). Uploads to the fixed self-contained path
/// <see cref="DevicePaths.FiveKeyTest"/> unless overridden via <c>MK20_UPLOAD_DEVICE_PATH</c>
/// - reused across scenarios so a stray/leftover run doesn't grow the SD card's used space.
/// Requires <c>MK20_COM_PORT</c> - see <see cref="HardwareConnection"/>. Formerly
/// <c>Mk20Control.App</c> menu option 13.
/// </summary>
public class UploadThemeFileTests
{
    [Test]
    public async Task UploadThemeFile_ActivatesSuccessfully()
    {
        string? localPath = Environment.GetEnvironmentVariable("MK20_UPLOAD_LOCAL_PATH");
        if (string.IsNullOrWhiteSpace(localPath))
            Assert.Ignore("Set MK20_UPLOAD_LOCAL_PATH to the local .Theme file to upload to run this test.");
        if (!File.Exists(localPath))
            Assert.Fail($"File not found: {localPath}");

        string devicePath = DevicePaths.Resolve(DevicePaths.FiveKeyTest);

        await using var client = await HardwareConnection.OpenAsync();

        byte[] bytes = File.ReadAllBytes(localPath);
        TestContext.WriteLine($"Uploading {bytes.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath, bytes, TimeSpan.FromSeconds(30));

        TestContext.WriteLine("Upload complete and theme activated.");
    }
}
