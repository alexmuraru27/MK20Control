namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// A key action whose "type" value has not yet been observed/confirmed against real
/// hardware. All decoded fields are still available via <see cref="KeyAction.RawFields"/> -
/// this type exists so unrecognized actions are surfaced explicitly rather than silently
/// dropped or mis-mapped to an existing action type.
/// </summary>
public sealed record UnknownKeyAction : KeyAction;
