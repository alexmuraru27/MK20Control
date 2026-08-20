using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Framing;
using Mk20Control.Protocol.Host;
using Mk20Control.Protocol.Model;
using Mk20Control.Protocol.Transport;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Replays REAL device bytes - lifted verbatim from a live USB capture of the MK20 reporting
/// key presses (tools/Captures/capture22_text_input.pcapng) - through the full production
/// path <c>transport bytes -> DeviceFrameParser -> Mk20DeviceClient -> KeyBindings -> your
/// handler</c>, with no device attached.
///
/// This is the offline proof that command-id dispatch works end to end against genuine
/// hardware output rather than against bytes this codebase invented: the captured frames
/// carry arbitrary strings in <c>inputText</c> (<c>"#clear"</c>, <c>"#ws"</c>), which is
/// exactly the channel <see cref="Mk20Control.Protocol.Theme.Building.KeyActions.Command"/>
/// uses to carry a command id back to the host.
/// </summary>
public class KeyBindingsReplayTests
{
    /// <summary>Real capture: row 0, col 0, pressed=1, inputText="#clear".</summary>
    private const string ClearPress =
        "AAAAAgAAAAQAAAAIAHQAeQBwAGUAAAAKAAAAABAAawBlAHkAUwB0AGEAdABlAAAABgByAG8AdwAAAAIAAAAAAAAAAA4AcAByAGUAcwBzAGUAZAAAAAIAAAAAAQAAAAYAYwBvAGwAAAACAAAAAAAAAAAIAAAACAB0AHkAcABlAAAACgAAAAAIAHQAZQB4AHQAAAAiAHAAYQByAGUAbgB0AEQAZQBzAGMAcgBpAHAAdABpAG8AbgAAAAoAAAAAKABTAHkAcwB0AGUAbQAgAGkAbgBwAHUAdAAgAGMAbwBuAHQAcgBvAGwAAAAYAGkAcwBJAG4AcAB1AHQARQBuAHQAZQByAAAAAQAAAAAAFgBpAHMAQwBvAHAAeQBQAGEAcwB0AGUAAAABAAAAAAASAGkAbgBwAHUAdABUAGUAeAB0AAAACgAAAAAMACMAYwBsAGUAYQByAAAAEABpAGMAbwBuAFAAYQB0AGgAAAAKAAAAADQALwBzAHQAYQB0AGkAYwAvAGkAYwBvAG4ALwBkAGEAcgBrAC8AVABlAHgAdAAuAHAAbgBnAAAAFgBkAGUAcwBjAHIAaQBwAHQAaQBvAG4AAAAKAAAAAAgAVABlAHgAdAAAACoAQQBJAFMAbwB1AG4AZABDAG8AbgB0AHIAbwBsAEsAZQB5AHcAbwByAGQAAAAKAAAAAAA=";

    /// <summary>Real capture: the matching release of the same key (pressed=0).</summary>
    private const string ClearRelease =
        "AAAAAgAAAAQAAAAIAHQAeQBwAGUAAAAKAAAAABAAawBlAHkAUwB0AGEAdABlAAAABgByAG8AdwAAAAIAAAAAAAAAAA4AcAByAGUAcwBzAGUAZAAAAAIAAAAAAAAAAAYAYwBvAGwAAAACAAAAAAAAAAAIAAAACAB0AHkAcABlAAAACgAAAAAIAHQAZQB4AHQAAAAiAHAAYQByAGUAbgB0AEQAZQBzAGMAcgBpAHAAdABpAG8AbgAAAAoAAAAAKABTAHkAcwB0AGUAbQAgAGkAbgBwAHUAdAAgAGMAbwBuAHQAcgBvAGwAAAAYAGkAcwBJAG4AcAB1AHQARQBuAHQAZQByAAAAAQAAAAAAFgBpAHMAQwBvAHAAeQBQAGEAcwB0AGUAAAABAAAAAAASAGkAbgBwAHUAdABUAGUAeAB0AAAACgAAAAAMACMAYwBsAGUAYQByAAAAEABpAGMAbwBuAFAAYQB0AGgAAAAKAAAAADQALwBzAHQAYQB0AGkAYwAvAGkAYwBvAG4ALwBkAGEAcgBrAC8AVABlAHgAdAAuAHAAbgBnAAAAFgBkAGUAcwBjAHIAaQBwAHQAaQBvAG4AAAAKAAAAAAgAVABlAHgAdAAAACoAQQBJAFMAbwB1AG4AZABDAG8AbgB0AHIAbwBsAEsAZQB5AHcAbwByAGQAAAAKAAAAAAA=";

    /// <summary>Real capture: a DIFFERENT key (row 0, col 3), pressed=1, inputText="#ws".</summary>
    private const string WsPress =
        "AAAAAgAAAAQAAAAIAHQAeQBwAGUAAAAKAAAAABAAawBlAHkAUwB0AGEAdABlAAAABgByAG8AdwAAAAIAAAAAAAAAAA4AcAByAGUAcwBzAGUAZAAAAAIAAAAAAQAAAAYAYwBvAGwAAAACAAAAAAMAAAAIAAAACAB0AHkAcABlAAAACgAAAAAIAHQAZQB4AHQAAAAiAHAAYQByAGUAbgB0AEQAZQBzAGMAcgBpAHAAdABpAG8AbgAAAAoAAAAAKABTAHkAcwB0AGUAbQAgAGkAbgBwAHUAdAAgAGMAbwBuAHQAcgBvAGwAAAAYAGkAcwBJAG4AcAB1AHQARQBuAHQAZQByAAAAAQAAAAAAFgBpAHMAQwBvAHAAeQBQAGEAcwB0AGUAAAABAAAAAAASAGkAbgBwAHUAdABUAGUAeAB0AAAACgAAAAAGACMAdwBzAAAAEABpAGMAbwBuAFAAYQB0AGgAAAAKAAAAADQALwBzAHQAYQB0AGkAYwAvAGkAYwBvAG4ALwBkAGEAcgBrAC8AVABlAHgAdAAuAHAAbgBnAAAAFgBkAGUAcwBjAHIAaQBwAHQAaQBvAG4AAAAKAAAAAAgAVABlAHgAdAAAACoAQQBJAFMAbwB1AG4AZABDAG8AbgB0AHIAbwBsAEsAZQB5AHcAbwByAGQAAAAKAAAAAAA=";

    /// <summary>Feeds bytes to the client exactly as a serial port would, without a device.</summary>
    private sealed class ReplayTransport : ISerialTransport
    {
        public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
        public event EventHandler<Exception>? ErrorOccurred;

        public bool IsOpen { get; private set; }
        public Task OpenAsync(CancellationToken cancellationToken = default) { IsOpen = true; return Task.CompletedTask; }
        public Task CloseAsync(CancellationToken cancellationToken = default) { IsOpen = false; return Task.CompletedTask; }
        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;

        /// <summary>Wraps a captured payload in the real frame header and delivers it.</summary>
        public void Deliver(string payloadBase64)
        {
            var frame = new DeviceFrame(
                PacketType: (uint)PacketType.AckReply,
                CommandId: (uint)CommandId.DeviceProactiveEscalationCommand,
                Payload: Convert.FromBase64String(payloadBase64),
                DeclaredChecksum: 0,
                IsChecksumValid: true);

            DataReceived?.Invoke(this, frame.Encode());
        }

        public void RaiseError(Exception ex) => ErrorOccurred?.Invoke(this, ex);
    }

    [Test]
    public async Task CapturedPresses_ReachTheHandlerBoundToTheirId()
    {
        var transport = new ReplayTransport();
        await using var client = new Mk20DeviceClient(transport);
        await client.ConnectAsync();

        var log = new List<string>();
        using var buttons = new KeyBindings(client);
        buttons.OnCommand("#clear", ctx => log.Add($"clear down @r{ctx.Position.Row}c{ctx.Position.Column}"));
        buttons.OnCommandRelease("#clear", ctx => log.Add($"clear up @r{ctx.Position.Row}c{ctx.Position.Column}"));
        buttons.OnCommand("#ws", ctx => log.Add($"ws down @r{ctx.Position.Row}c{ctx.Position.Column}"));

        transport.Deliver(ClearPress);
        transport.Deliver(ClearRelease);
        transport.Deliver(WsPress);

        Assert.That(log, Is.EqualTo(new[]
        {
            "clear down @r0c0",
            "clear up @r0c0",
            "ws down @r0c3",
        }));
    }

    [Test]
    public async Task AnIdWithNoBinding_RaisesUnboundCarryingThatId()
    {
        var transport = new ReplayTransport();
        await using var client = new Mk20DeviceClient(transport);
        await client.ConnectAsync();

        var unbound = new List<string?>();
        using var buttons = new KeyBindings(client);
        buttons.OnCommand("#clear", () => { });
        buttons.Unbound += (_, ctx) => unbound.Add(ctx.CommandId);

        transport.Deliver(ClearPress);   // bound - must not appear
        transport.Deliver(WsPress);      // not bound
        transport.Deliver(ClearRelease); // press is bound, release is not

        Assert.That(unbound, Is.EqualTo(new[] { "#ws", "#clear" }));
    }

    [Test]
    public async Task AThrowingHandler_DoesNotBreakTheReadLoop()
    {
        var transport = new ReplayTransport();
        await using var client = new Mk20DeviceClient(transport);
        await client.ConnectAsync();

        var log = new List<string>();
        using var buttons = new KeyBindings(client);
        buttons.OnCommand("#clear", () => throw new InvalidOperationException("handler blew up"));
        buttons.OnCommand("#ws", () => log.Add("ws still ran"));

        Assert.DoesNotThrow(() => transport.Deliver(ClearPress));
        transport.Deliver(WsPress);

        Assert.That(log, Is.EqualTo(new[] { "ws still ran" }));
    }

    [Test]
    public async Task UnbindAndClear_StopDelivery()
    {
        var transport = new ReplayTransport();
        await using var client = new Mk20DeviceClient(transport);
        await client.ConnectAsync();

        int hits = 0;
        using var buttons = new KeyBindings(client);
        buttons.OnCommand("#clear", () => hits++);
        buttons.OnCommand("#ws", () => hits++);

        transport.Deliver(ClearPress);
        Assert.That(hits, Is.EqualTo(1));

        Assert.That(buttons.Unbind("#clear"), Is.True);
        transport.Deliver(ClearPress);
        Assert.That(hits, Is.EqualTo(1), "unbound id must no longer be delivered");

        buttons.Clear();
        transport.Deliver(WsPress);
        Assert.That(hits, Is.EqualTo(1), "Clear() must remove every binding");
        Assert.That(buttons.BoundCommands, Is.Empty);
    }

    [Test]
    public async Task DisposingTheBindings_DetachesFromTheClient()
    {
        var transport = new ReplayTransport();
        await using var client = new Mk20DeviceClient(transport);
        await client.ConnectAsync();

        int hits = 0;
        var buttons = new KeyBindings(client);
        buttons.OnCommand("#clear", () => hits++);

        transport.Deliver(ClearPress);
        buttons.Dispose();
        transport.Deliver(ClearPress);

        Assert.That(hits, Is.EqualTo(1));
    }
}
