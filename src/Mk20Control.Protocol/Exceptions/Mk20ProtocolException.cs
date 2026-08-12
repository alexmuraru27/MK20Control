using System;

namespace Mk20Control.Protocol.Exceptions;

/// <summary>Base type for all exceptions raised by <see cref="Mk20Control.Protocol.Client.Mk20DeviceClient"/> and the codecs it uses.</summary>
public class Mk20ProtocolException : Exception
{
    public Mk20ProtocolException(string message) : base(message) { }
    public Mk20ProtocolException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when a request to the device did not receive a matching reply within the allotted time.</summary>
public sealed class Mk20TimeoutException : Mk20ProtocolException
{
    public Mk20TimeoutException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a client operation depends on protocol behavior that has not been confirmed
/// against real hardware (see the operation's XML documentation for details) and the client
/// has not been explicitly configured to allow experimental operations.
/// </summary>
public sealed class Mk20UnconfirmedOperationException : Mk20ProtocolException
{
    public Mk20UnconfirmedOperationException(string message) : base(message) { }
}

/// <summary>Thrown when a received frame's payload fails CRC-32 validation.</summary>
public sealed class Mk20ChecksumException : Mk20ProtocolException
{
    public Mk20ChecksumException(string message) : base(message) { }
}
