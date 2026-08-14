using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Host;
using Mk20Control.Protocol.Model;
using Mk20Control.Protocol.Theme.Actions;
using Mk20Control.Protocol.Theme.Building;
using Mk20Control.Protocol.Theme.Items;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Covers binding caller-supplied C# to buttons by COMMAND ID.
///
/// The reason ids exist rather than grid positions: the device's press event reports only
/// <c>{row, col, pressed}</c> and never says which page it came from, so two buttons in the
/// same cell on different pages are indistinguishable by position. An id stored on the key
/// travels with it and is echoed back on press.
/// </summary>
public class KeyBindingsTests
{
    /// <summary>
    /// Mirrors KeyBindings' dispatch without a live device: match on the id carried by the
    /// echoed action, else fall through to the unbound path.
    /// </summary>
    private sealed class Router
    {
        private readonly Dictionary<(string, bool), Action<KeyEventContext>> _handlers = new();
        public List<string> Unbound { get; } = new();
        public List<string> Errors { get; } = new();

        public void Bind(string id, bool pressed, Action<KeyEventContext> h) => _handlers[(id, pressed)] = h;

        public void Fire(KeyAction? action, int row = 0, int col = 0, bool pressed = true)
        {
            string? id = action is TextInputAction { InputText.Length: > 0 } t ? t.InputText : null;
            var ctx = new KeyEventContext(new KeyPosition(row, col), pressed, action, id);

            if (id is null || !_handlers.TryGetValue((id, pressed), out var h))
            {
                Unbound.Add($"{id ?? "(no id)"}:{(pressed ? "down" : "up")}");
                return;
            }
            try { h(ctx); }
            catch (Exception ex) { Errors.Add(ex.Message); }
        }
    }

    [Test]
    public void CommandId_RoutesToItsOwnHandler()
    {
        var fired = new List<string>();
        var router = new Router();
        router.Bind("pit.request", true, _ => fired.Add("pit"));
        router.Bind("tc.up", true, _ => fired.Add("tc"));

        router.Fire(KeyActions.Command("pit.request"));
        router.Fire(KeyActions.Command("tc.up"));
        router.Fire(KeyActions.Command("unbound.command"));

        Assert.That(fired, Is.EqualTo(new[] { "pit", "tc" }));
        Assert.That(router.Unbound, Is.EqualTo(new[] { "unbound.command:down" }));
    }

    [Test]
    public void SameCellOnDifferentPages_StillRoutesCorrectly()
    {
        // The whole point of ids: both presses report r0c0, and the device does not say which
        // page they came from - only the id distinguishes them.
        var fired = new List<string>();
        var router = new Router();
        router.Bind("page1.button", true, _ => fired.Add("page 1 button"));
        router.Bind("folder.button", true, _ => fired.Add("folder button"));

        router.Fire(KeyActions.Command("page1.button"), row: 0, col: 0);
        router.Fire(KeyActions.Command("folder.button"), row: 0, col: 0);

        Assert.That(fired, Is.EqualTo(new[] { "page 1 button", "folder button" }));
    }

    [Test]
    public void PressAndRelease_AreBoundIndependently()
    {
        var fired = new List<string>();
        var router = new Router();
        router.Bind("clutch", true, _ => fired.Add("down"));
        router.Bind("clutch", false, _ => fired.Add("up"));

        router.Fire(KeyActions.Command("clutch"), pressed: true);
        router.Fire(KeyActions.Command("clutch"), pressed: false);

        Assert.That(fired, Is.EqualTo(new[] { "down", "up" }));
    }

    [Test]
    public void ReleaseWithoutABinding_FallsThroughToUnbound()
    {
        var fired = new List<string>();
        var router = new Router();
        router.Bind("drs", true, _ => fired.Add("down"));

        router.Fire(KeyActions.Command("drs"), pressed: true);
        router.Fire(KeyActions.Command("drs"), pressed: false);

        Assert.That(fired, Is.EqualTo(new[] { "down" }));
        Assert.That(router.Unbound, Is.EqualTo(new[] { "drs:up" }));
    }

    [Test]
    public void DeviceNativeActions_CarryNoIdAndFallThrough()
    {
        // Keyboard and navigation keys are executed by the device itself; they have no command
        // id, so they never match a binding.
        var router = new Router();
        router.Bind("anything", true, _ => Assert.Fail("must not fire"));

        router.Fire(KeyActions.Keyboard(HidKey.A, "A"));
        router.Fire(KeyActions.NextPage());
        router.Fire(KeyActions.OneLevelUp());

        Assert.That(router.Unbound, Has.Count.EqualTo(3));
    }

    [Test]
    public void AThrowingHandler_IsContainedAndOthersStillFire()
    {
        var fired = new List<string>();
        var router = new Router();
        router.Bind("boom", true, _ => throw new InvalidOperationException("boom"));
        router.Bind("fine", true, _ => fired.Add("still works"));

        router.Fire(KeyActions.Command("boom"));
        router.Fire(KeyActions.Command("fine"));

        Assert.That(router.Errors, Is.EqualTo(new[] { "boom" }));
        Assert.That(fired, Is.EqualTo(new[] { "still works" }));
    }

    [Test]
    public void CommandId_SurvivesAThemeRoundTrip()
    {
        // The id must survive being written to a .Theme and decoded back, since that is how it
        // reaches the device and returns on press.
        var builder = new ThemeBuilder();
        builder.AddPage(page =>
        {
            page.SetCanvas(640, 656);
            page.AddKey(0, 0, key => key.Title("PIT").Action(KeyActions.Command("pit.request")));
            page.AddKey(0, 1, key => key.Title("TC+").Action(KeyActions.Command("tc.up")));
        });

        var decoded = ThemeFileCodec.Decode(ThemeFileCodec.Encode(builder.Build()));
        var ids = decoded.Pages[0].Items.OfType<KeyItem>()
            .Select(k => k.Action).OfType<TextInputAction>()
            .Select(a => a.InputText)
            .ToList();

        Assert.That(ids, Is.EqualTo(new[] { "pit.request", "tc.up" }));
    }

    [Test]
    public void Command_RejectsAnEmptyId()
    {
        // An empty id would be indistinguishable from "no id" on the wire.
        Assert.Throws<ArgumentException>(() => KeyActions.Command(""));
        Assert.Throws<ArgumentException>(() => KeyActions.Command("   "));
    }

    [Test]
    public void Command_ProducesAnActionTheDeviceWillReportButNotExecute()
    {
        var action = KeyActions.Command("pit.request");

        Assert.Multiple(() =>
        {
            // "text" is the one action type the device delegates rather than performing.
            Assert.That(action.RawType, Is.EqualTo("text"));
            Assert.That(action.InputText, Is.EqualTo("pit.request"));
            // Nothing should be auto-typed on the host's behalf.
            Assert.That(action.IsInputEnter, Is.False);
            Assert.That(action.IsCopyPaste, Is.False);
        });
    }
}
