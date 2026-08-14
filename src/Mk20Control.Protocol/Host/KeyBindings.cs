using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Model;
using Mk20Control.Protocol.Theme.Actions;

namespace Mk20Control.Protocol.Host;

/// <summary>
/// Runs your own C# when a button is pressed, identified by a caller-defined COMMAND ID that
/// travels with the button:
///
/// <code>
/// // when building the theme - the id is stored on the key itself
/// page.AddKey(0, 0, key => key.Icon(...).Title("PIT").Action(KeyActions.Command("pit.request")));
///
/// // at runtime
/// using var buttons = new KeyBindings(client);
/// buttons.OnCommand("pit.request", () =&gt; sim.RequestPitStop());
/// buttons.OnCommand("tc.up",       () =&gt; sim.TractionControlUp());
/// </code>
///
/// IDs are deliberately the ONLY way to bind, because they are page-agnostic. The device's
/// press event reports just <c>{row, col, pressed}</c> and does NOT say which page it came
/// from (confirmed by decoding real captures), so binding by grid position would silently fire
/// the wrong handler as soon as a theme has more than one page - r0c0 on the first page and
/// r0c0 inside a folder are indistinguishable by position. With IDs, 50 buttons spread across
/// pages and folders each stay distinct, and a button keeps its behaviour when you move it.
///
/// Handlers run on the transport's read thread. Keep them short and non-blocking; queue slow
/// work to your own worker. Exceptions are caught and logged so one bad handler cannot kill
/// the read loop or stop other bindings firing.
/// </summary>
public sealed class KeyBindings : IDisposable
{
    private readonly Mk20DeviceClient _client;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<(string Id, bool Pressed), Action<KeyEventContext>> _handlers = new();
    private bool _disposed;

    /// <summary>Runs for every reported key event that matched no binding - useful for logging or a catch-all.</summary>
    public event EventHandler<KeyEventContext>? Unbound;

    public KeyBindings(Mk20DeviceClient client, ILogger<KeyBindings>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _client.NotificationReceived += OnNotification;
    }

    /// <summary>Runs <paramref name="handler"/> when the button carrying <paramref name="commandId"/> is pressed, wherever it lives.</summary>
    public KeyBindings OnCommand(string commandId, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnCommand(commandId, _ => handler());
    }

    /// <summary>Runs <paramref name="handler"/> on press, passing the event's details.</summary>
    public KeyBindings OnCommand(string commandId, Action<KeyEventContext> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[(commandId, true)] = handler;
        return this;
    }

    /// <summary>Runs <paramref name="handler"/> when the button carrying <paramref name="commandId"/> is released.</summary>
    public KeyBindings OnCommandRelease(string commandId, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnCommandRelease(commandId, _ => handler());
    }

    /// <summary>Runs <paramref name="handler"/> on release, passing the event's details.</summary>
    public KeyBindings OnCommandRelease(string commandId, Action<KeyEventContext> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[(commandId, false)] = handler;
        return this;
    }

    /// <summary>Removes the press and release bindings for a command id. Returns true if anything was bound.</summary>
    public bool Unbind(string commandId)
    {
        bool press = _handlers.TryRemove((commandId, true), out _);
        bool release = _handlers.TryRemove((commandId, false), out _);
        return press || release;
    }

    /// <summary>Removes every binding.</summary>
    public void Clear() => _handlers.Clear();

    /// <summary>The command ids currently bound, as (id, isPress) pairs.</summary>
    public IReadOnlyCollection<(string Id, bool Pressed)> BoundCommands => _handlers.Keys.ToArray();

    private void OnNotification(object? sender, DeviceNotificationEventArgs e)
    {
        string? commandId = ExtractCommandId(e.Action);
        var context = new KeyEventContext(e.Position, e.IsPressed, e.Action, commandId);

        if (commandId is not null && _handlers.TryGetValue((commandId, e.IsPressed), out var handler))
        {
            try
            {
                handler(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handler for command '{CommandId}' ({State}) threw.",
                    commandId, e.IsPressed ? "press" : "release");
            }
            return;
        }

        Unbound?.Invoke(this, context);
    }

    /// <summary>
    /// Pulls the caller-defined id out of the echoed action descriptor. The device returns the
    /// same field set the theme stored, so a key built with <c>KeyActions.Command("x")</c>
    /// reports back <c>inputText="x"</c> on press.
    /// </summary>
    private static string? ExtractCommandId(KeyAction? action) =>
        action is TextInputAction { InputText.Length: > 0 } text ? text.InputText : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.NotificationReceived -= OnNotification;
        Clear();
    }
}

/// <summary>Details of the key event that triggered a binding.</summary>
/// <param name="Position">Which physical cell, as zero-based row/column. NOT page-qualified - the device does not report the page, which is why bindings use <see cref="CommandId"/>.</param>
/// <param name="IsPressed">True for press, false for release.</param>
/// <param name="Action">The key's action from the loaded theme, if it reported one.</param>
/// <param name="CommandId">The caller-defined id set via <c>KeyActions.Command(...)</c>, if this key carries one.</param>
public readonly record struct KeyEventContext(
    KeyPosition Position,
    bool IsPressed,
    KeyAction? Action,
    string? CommandId = null);
