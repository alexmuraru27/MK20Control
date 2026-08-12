using System;

namespace Mk20Control.Protocol.Client;

/// <summary>Configuration options for <see cref="Mk20DeviceClient"/>.</summary>
public sealed class Mk20DeviceClientOptions
{
    /// <summary>Serial line rate. Matches the vendor app's setting; over CDC-ACM this is typically a no-op for real throughput.</summary>
    public int BaudRate { get; init; } = 115200;

    /// <summary>Default timeout used for request/reply operations that don't specify their own.</summary>
    public TimeSpan DefaultRequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
