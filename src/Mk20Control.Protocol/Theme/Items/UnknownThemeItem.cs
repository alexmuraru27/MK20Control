namespace Mk20Control.Protocol.Theme.Items;

/// <summary>
/// A theme item whose "type" value has not yet been observed/confirmed against real
/// hardware. All original fields are still available via <see cref="ThemeItem.RawJson"/> -
/// this type exists so unrecognized items are surfaced explicitly rather than silently
/// dropped or mis-mapped to an existing item type.
/// </summary>
public sealed record UnknownThemeItem : ThemeItem;
