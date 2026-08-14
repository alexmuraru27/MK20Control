using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using Mk20Control.Protocol.Host;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// End-to-end proof of the command-id event API on real hardware: upload a theme whose keys
/// carry caller-defined ids (see <see cref="CommandThemeBuilderTests"/>), bind ordinary C# to
/// those ids, then press the buttons and watch the right handler run.
///
/// The theme deliberately puts DIFFERENT ids in the SAME grid cell on three different pages,
/// because that is the case ids exist for - the device's press event reports only
/// <c>{row, col, pressed}</c> and never says which page it came from, so r0c0 on page 1,
/// r0c0 on page 2 and r0c0 inside the folder are indistinguishable by position.
///
/// Requires <c>MK20_COM_PORT</c> (see <see cref="HardwareConnection"/>) and, for the listen
/// test, that ScreenKeyWindows is CLOSED - it holds the serial port exclusively.
/// Set <c>MK20_LISTEN_SECONDS</c> to override the default 30-second window.
/// </summary>
public class CommandBindingsHardwareTests
{
    [Test]
    public async Task BuildAndUpload_ActivatesCommandTheme()
    {
        string themeName = DeviceThemeNames.Resolve(DeviceThemeNames.Commands);
        byte[] encoded = CommandThemeBuilderTests.BuildCommandTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {themeName}...");
        await client.UploadThemeAsync(themeName, encoded, TimeSpan.FromSeconds(30));

        TestContext.WriteLine(
            "Upload complete and theme activated. Every key is labelled with the command id it " +
            "reports; NEXT/PREV page through, FOLDER enters the folder and BACK leaves it. " +
            "Run ListenByCommandId_RunsTheMatchingHandler to see the ids arrive.");
    }

    [Test]
    public async Task ListenByCommandId_RunsTheMatchingHandler()
    {
        await using var client = await HardwareConnection.OpenAsync();

        var fired = new List<string>();
        using var buttons = new KeyBindings(client);

        foreach (var (id, page, row, col) in CommandThemeBuilderTests.Commands)
        {
            string commandId = id;
            var location = $"page {page} r{row}c{col}";
            buttons.OnCommand(commandId, () =>
            {
                fired.Add(commandId);
                TestContext.WriteLine($"[bind] '{commandId}' pressed ({location}) -> my own C# ran");
            });
        }

        buttons.Unbound += (_, ctx) => TestContext.WriteLine(
            $"[----] {ctx.Position} pressed={ctx.IsPressed} id={ctx.CommandId ?? "(none - device-native action)"}");

        client.PageSwitched += (_, _) => TestContext.WriteLine("[page] the active page CHANGED");

        int seconds = int.TryParse(Environment.GetEnvironmentVariable("MK20_LISTEN_SECONDS"), out int s) ? s : 30;
        TestContext.WriteLine(
            $"Listening for {seconds}s. Press the labelled keys - including the SAME cell on " +
            "page 1, page 2 and inside the folder, which must produce three different ids.");
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        TestContext.WriteLine($"Handlers ran for: {(fired.Count == 0 ? "(nothing pressed)" : string.Join(", ", fired.Distinct()))}");
    }
}
