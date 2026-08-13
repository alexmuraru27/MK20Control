using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Theme.Items;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Decodes a local <c>.Theme</c> file and prints its structure (pages, items, key actions) -
/// a diagnostic aid, not a correctness assertion about a specific file. Uses the theme built
/// by <see cref="FiveKeyTestThemeTests"/> as a concrete example so the test is
/// self-contained. No hardware required. Formerly <c>Mk20Control.App</c> menu option 10 /
/// <c>DecodeLocalTheme</c>.
/// </summary>
public class DecodeLocalThemeTests
{
    [Test]
    public void DecodeLocalTheme_PrintsStructure()
    {
        byte[] bytes = FiveKeyTestThemeTests.BuildFiveKeyTestTheme();
        var theme = ThemeFileCodec.Decode(bytes);

        TestContext.WriteLine($"Language={theme.Language} LayoutVersion={theme.LayoutVersion} " +
            $"Pages={theme.Pages.Count} Assets={theme.Assets.Count} CurrentPageId={theme.CurrentPageId}");

        Assert.That(theme.Pages, Is.Not.Empty);

        foreach (var page in theme.Pages)
        {
            TestContext.WriteLine($"  Page {page.PageName}: {page.Items.Count} items");
            foreach (var item in page.Items.OfType<KeyItem>())
            {
                string actionDesc = DescribeAction(item);
                TestContext.WriteLine($"    key row={item.Row} col={item.Column} icon={item.IconAssetPath}: {actionDesc}");
            }
        }
    }

    private static string DescribeAction(KeyItem item) => item.Action switch
    {
        Mk20Control.Protocol.Theme.Actions.KeyboardAction k => $"keyboard '{k.KeyLabel}' (keycode {k.Keycode})",
        Mk20Control.Protocol.Theme.Actions.OpenWebAction w => $"open web {w.Url}",
        Mk20Control.Protocol.Theme.Actions.MouseAction => "mouse action",
        Mk20Control.Protocol.Theme.Actions.PageSwitchAction p => $"page switch (mode {p.PageSwitchMode})",
        Mk20Control.Protocol.Theme.Actions.AudioVolumeAction a => $"{a.DeviceClass} volume ({a.TargetDeviceName})",
        Mk20Control.Protocol.Theme.Actions.TextInputAction t => $"type text '{t.InputText}'",
        Mk20Control.Protocol.Theme.Actions.KeyboardSwitchAction => "switch keyboard layout",
        Mk20Control.Protocol.Theme.Actions.OpenPageAction op => $"open page {op.PageName}",
        Mk20Control.Protocol.Theme.Actions.OneLevelUpAction => "navigate to parent page",
        Mk20Control.Protocol.Theme.Actions.ControlFlowAction => "control flow (macro)",
        Mk20Control.Protocol.Theme.Actions.EncoderKeyboardAction ek => $"encoder keyboard (left={ek.LeftKeyLabel} middle={ek.MiddleKeyLabel} right={ek.RightKeyLabel})",
        Mk20Control.Protocol.Theme.Actions.EncoderFunctionAction ef => $"encoder function ({ef.RawType})",
        Mk20Control.Protocol.Theme.Actions.UnknownKeyAction u => $"unrecognized action type '{u.RawType}'",
        null => "(no action)",
        _ => "(action)",
    };
}
