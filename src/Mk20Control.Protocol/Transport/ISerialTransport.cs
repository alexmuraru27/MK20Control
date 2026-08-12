using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mk20Control.Protocol.Transport;

/// <summary>
/// Abstraction over the byte-stream transport used to talk to an MK20 device (in practice a
/// USB CDC-ACM serial port), so <see cref="Client.Mk20DeviceClient"/> can be unit tested
/// without a real device attached.
/// </summary>
public interface ISerialTransport : IAsyncDisposable
{
    /// <summary>Raised whenever new bytes are received from the transport.</summary>
    event EventHandler<ReadOnlyMemory<byte>>? DataReceived;

    /// <summary>Raised when the read loop encounters an unexpected error (the transport remains open; callers may choose to reconnect).</summary>
    event EventHandler<Exception>? ErrorOccurred;

    bool IsOpen { get; }

    Task OpenAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
