using System;
using System.Collections.Generic;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Model;

namespace Mk20Control.Protocol.Client;

/// <summary>
/// Raised for a decoded DEVICE_ProactiveEscalationCMD event: a physical key was
/// pressed/released (or an encoder-assigned function was activated). The first tagged-value
/// map in the payload is always a "keyState" descriptor ({row, col, pressed}); a second map
/// describing the bound action/function is present when the key/encoder has a rich action
/// assigned (page-switch, encoder function) - it is absent for keys without any wire-visible
/// action bound (see the confirmed finding that plain keys with no assigned action produce
/// no traffic at all).
/// </summary>
public sealed class DeviceNotificationEventArgs : EventArgs
{
    public required KeyPosition Position { get; init; }
    public required bool IsPressed { get; init; }

    /// <summary>
    /// The second map's fields (the bound action/function descriptor), if present. This is
    /// the RAW decoded field set - it is deliberately NOT unified with the
    /// <c>Mk20Control.Protocol.Theme.Actions.KeyAction</c> hierarchy (used for .Theme file
    /// <c>controlData</c>), since the two shapes have only been observed to partially
    /// overlap and unifying them would require assuming a correspondence that hasn't been
    /// individually confirmed for every action type.
    /// </summary>
    public IReadOnlyDictionary<string, TaggedValue>? ActionDescriptor { get; init; }

    /// <summary>The complete, unprocessed decoded map array, for anything not exposed above.</summary>
    public required IReadOnlyList<IReadOnlyDictionary<string, TaggedValue>> RawMaps { get; init; }
}
