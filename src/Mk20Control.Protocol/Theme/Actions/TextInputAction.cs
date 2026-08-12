namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>A key assigned to type literal text into the host ("type": "text").</summary>
public sealed record TextInputAction : KeyAction
{
    public required string InputText { get; init; }
    public bool IsInputEnter { get; init; }
    public bool IsCopyPaste { get; init; }
}
