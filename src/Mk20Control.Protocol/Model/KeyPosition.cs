namespace Mk20Control.Protocol.Model;

/// <summary>A physical key's matrix position, as reported in DEVICE_ProactiveEscalationCMD key events and KeyItem theme entries.</summary>
public readonly record struct KeyPosition(int Row, int Column)
{
    public override string ToString() => $"(row={Row}, col={Column})";
}
