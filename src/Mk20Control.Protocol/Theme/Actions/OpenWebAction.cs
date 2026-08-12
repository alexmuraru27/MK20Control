namespace Mk20Control.Protocol.Theme.Actions;

/// <summary>A key assigned to open a URL in the host's default browser ("type": "openWeb").</summary>
public sealed record OpenWebAction : KeyAction
{
    public required string Url { get; init; }
}
