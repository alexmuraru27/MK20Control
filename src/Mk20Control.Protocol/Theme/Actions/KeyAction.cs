using Mk20Control.Protocol.Codecs;
using System.Collections.Generic;

namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// Base type for a physical key's assigned action, decoded from a <c>KeyItem.ControlData</c>
/// tagged-value map (see <see cref="VariantMapCodec"/>). Seven concrete action types have
/// been confirmed against real hardware captures so far (see the derived types in this
/// namespace); any other "type" value decodes to <see cref="UnknownKeyAction"/> rather than
/// being guessed at or dropped.
/// </summary>
public abstract record KeyAction
{
    /// <summary>The original "type" field value (e.g. "keyboard", "openWeb", "qmk_mouse").</summary>
    public required string RawType { get; init; }

    public string? Description { get; init; }
    public string? ParentDescription { get; init; }
    public string? IconPath { get; init; }

    /// <summary>
    /// The complete set of fields as decoded from the tagged-value map, always present -
    /// use this to access any field not yet promoted to a strongly-typed property.
    /// </summary>
    public required IReadOnlyDictionary<string, TaggedValue> RawFields { get; init; }
}
