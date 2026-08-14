using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// Live experiment answering "can an encoder be mapped to something other than the three
/// predefined functions?" - uploads <see cref="EncoderCommandThemeTests"/>' theme (left
/// encoder = a plain command id, right encoder = a command id marked
/// <c>category="encoder"</c>) and logs every raw device event while you turn and click them.
///
/// Encoder activity arrives as pseudo-rows 100-105 with <c>col == row</c> and no release, so
/// this test prints the raw row/col rather than binding, to show exactly what the device
/// reports. Requires <c>MK20_COM_PORT</c> and ScreenKeyWindows closed.
/// </summary>
public class EncoderCommandHardwareTests
{
    [Test]
    public async Task UploadAndObserve_WhatTheEncodersReport()
    {
        string devicePath = DevicePaths.Resolve(DevicePaths.EncoderCommands);
        byte[] encoded = EncoderCommandThemeTests.BuildTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {devicePath}...");
        await client.UploadThemeFileAsync(devicePath, encoded, TimeSpan.FromSeconds(30));

        int events = 0;
        client.NotificationReceived += (_, e) =>
        {
            events++;
            string id = e.Action is Mk20Control.Protocol.Theme.Actions.TextInputAction t ? t.InputText : "(no command id)";
            TestContext.WriteLine(
                $"[evt] row={e.Position.Row} col={e.Position.Column} pressed={e.IsPressed} " +
                $"type={e.Action?.RawType ?? "(none)"} id={id}");
        };

        int seconds = int.TryParse(Environment.GetEnvironmentVariable("MK20_LISTEN_SECONDS"), out int s) ? s : 40;
        TestContext.WriteLine($"Listening {seconds}s. ROTATION ONLY - do NOT click the knobs:");
        TestContext.WriteLine("   1. Turn the LEFT knob ~5 clicks CLOCKWISE");
        TestContext.WriteLine("   2. Turn the LEFT knob ~5 clicks COUNTER-CLOCKWISE");
        TestContext.WriteLine("   3. Turn the RIGHT knob ~5 clicks CLOCKWISE");
        TestContext.WriteLine("   4. Turn the RIGHT knob ~5 clicks COUNTER-CLOCKWISE");
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        TestContext.WriteLine($"Captured {events} event(s).");
    }
}
