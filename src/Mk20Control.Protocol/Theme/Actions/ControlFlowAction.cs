using System;

namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>
/// A key assigned to a "control flow" (multi-step macro/automation) - "type": "ControlFlow".
/// Confirmed present on <c>defaultTheme.Theme</c> (description "操作流" = "Operation flow"),
/// but the key in the file examined had never actually been configured with any steps: its
/// "controlDataList" field decoded to just 4 zero bytes (base64 "AAAAAA=="), consistent with
/// an empty list header rather than real step data. The exact schema for a *populated*
/// control-flow step list has NOT been observed and is therefore NOT modeled further here -
/// <see cref="ControlDataList"/> exposes the raw decoded bytes as-is (never guessed at) so
/// nothing is lost; re-capture a theme with an actually-configured control-flow key to
/// extend this type once real step data is available.
/// </summary>
public sealed record ControlFlowAction : KeyAction
{
    /// <summary>The raw, not-yet-decoded bytes of the "controlDataList" field (see remarks on why this isn't further parsed).</summary>
    public byte[]? ControlDataList { get; init; }
}
