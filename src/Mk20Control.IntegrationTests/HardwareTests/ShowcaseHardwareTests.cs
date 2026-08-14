using Mk20Control.IntegrationTests.OfflineThemeTests;
using Mk20Control.IntegrationTests.Support;
using Mk20Control.Protocol.Host;
using Mk20Control.Protocol.Theme.Building;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.HardwareTests;

/// <summary>
/// The full end-to-end demo on real hardware: uploads <see cref="ShowcaseThemeTests"/>' theme
/// (animated GIF on both screens, alpha-PNG button icons with text titles, both encoders and
/// every button bound to a command id) and then binds ordinary C# to every one of those ids
/// through <see cref="KeyBindings"/>, so pressing anything on the device runs your code.
///
/// It is also the visual answer to "can button icons be transparent?": row 0 uses
/// <c>IconPreservingAlpha</c> (alpha kept) and row 1 uses the ordinary <c>Icon</c> path
/// (alpha flattened onto black) with the SAME ring artwork, so the two rows are a direct
/// side-by-side comparison against the animated background.
///
/// Requires <c>MK20_COM_PORT</c> and ScreenKeyWindows closed.
/// </summary>
public class ShowcaseHardwareTests
{
    [Test]
    public async Task UploadShowcase_AndRunMyOwnCodeForEveryControl()
    {
        string themeName = DeviceThemeNames.Resolve(DeviceThemeNames.Showcase);
        byte[] encoded = ShowcaseThemeTests.BuildShowcaseTheme();

        await using var client = await HardwareConnection.OpenAsync();
        TestContext.WriteLine($"Uploading {encoded.Length} bytes to {themeName}...");
        await client.UploadThemeAsync(themeName, encoded, TimeSpan.FromSeconds(60));
        TestContext.WriteLine("Uploaded and activated.");

        var fired = new List<string>();
        using var buttons = new KeyBindings(client);

        void Bind(string id, string what) => buttons.OnCommand(id, () =>
        {
            fired.Add(id);
            TestContext.WriteLine($"[mine] {what} -> my own C# ran (id '{id}')");
        });

        foreach (var (id, _, title) in ShowcaseThemeTests.AlphaButtons)
            Bind(id, $"alpha button '{title}'");

        Bind(ShowcaseThemeTests.OpaqueControlId, "opaque control button");
        for (int col = 0; col < 5; col++) Bind($"btn.{col}", $"row-1 button {col}");
        Bind(ShowcaseThemeTests.LeftEncoderId, "LEFT encoder");
        Bind(ShowcaseThemeTests.RightEncoderId, "RIGHT encoder");

        buttons.Unbound += (_, ctx) =>
        {
            string where = EncoderPositions.SideOfPseudoRow(ctx.Position.Row) is { } side
                ? $"{side} encoder"
                : $"r{ctx.Position.Row}c{ctx.Position.Column}";
            TestContext.WriteLine($"[----] unbound {where} pressed={ctx.IsPressed} id={ctx.CommandId ?? "(none)"}");
        };

        int seconds = int.TryParse(Environment.GetEnvironmentVariable("MK20_LISTEN_SECONDS"), out int s) ? s : 60;
        TestContext.WriteLine($"Listening {seconds}s - press the buttons and turn both knobs.");
        TestContext.WriteLine("LOOK AT THE SCREEN: row 0 icons keep their alpha, row 1 icons are flattened onto black.");
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        TestContext.WriteLine($"Handlers ran for: {(fired.Count == 0 ? "(nothing pressed)" : string.Join(", ", fired.Distinct()))}");
    }
}
