using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mk20Control.Protocol.Transport;

/// <summary>
/// Decorator over any <see cref="ISerialTransport"/> that logs every raw byte block written
/// and received, with high-precision timestamps, to a plain-text hex log file. This exists
/// as a live-USB-capture substitute: it records byte-for-byte exactly what this process
/// wrote to and read from the serial port, with the same fidelity a USB capture would show
/// for the underlying CDC-ACM bulk payloads - usable to directly compare a real test run
/// against confirmed real captures (see <c>tools/Captures/*.pcapng</c>) using the same
/// message-sequence analysis approach, without requiring OS-level USB capture privileges.
///
/// Log line format: "{elapsedSeconds:F6}\t{OUT|IN}\t{hexBytes}" - one line per
/// <see cref="ISerialTransport.WriteAsync"/> call or <see cref="ISerialTransport.DataReceived"/>
/// event, matching the granularity of a single USB bulk transfer as closely as this
/// process's own I/O boundaries allow.
/// </summary>
public sealed class WireLoggingTransport : ISerialTransport
{
    private readonly ISerialTransport _inner;
    private readonly StreamWriter _log;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly object _logLock = new();

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<Exception>? ErrorOccurred;

    public WireLoggingTransport(ISerialTransport inner, string logFilePath)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
        _inner = inner;
        _log = new StreamWriter(logFilePath, append: false) { AutoFlush = true };
        _inner.DataReceived += OnInnerDataReceived;
        _inner.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(this, e);
    }

    public bool IsOpen => _inner.IsOpen;

    public Task OpenAsync(CancellationToken cancellationToken = default) => _inner.OpenAsync(cancellationToken);
    public Task CloseAsync(CancellationToken cancellationToken = default) => _inner.CloseAsync(cancellationToken);

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        double startedAt = _stopwatch.Elapsed.TotalSeconds;
        LogLine("OUT", data.Span, startedAt);
        await _inner.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        // Also record when the write actually completed (relevant once the inner transport
        // provides real backpressure/draining, e.g. SerialPortTransport.DrainWriteBufferAsync)
        // - this can be meaningfully later than "started", unlike a naive fire-and-forget
        // SerialPort.Write() which returns as soon as bytes are queued.
        double completedAt = _stopwatch.Elapsed.TotalSeconds;
        if (completedAt - startedAt > 0.001)
        {
            lock (_logLock) { _log.WriteLine($"{completedAt:F6}\tOUT-DRAINED\t{data.Length}"); }
        }
    }

    private void OnInnerDataReceived(object? sender, ReadOnlyMemory<byte> data)
    {
        LogLine("IN", data.Span, _stopwatch.Elapsed.TotalSeconds);
        DataReceived?.Invoke(this, data);
    }

    private void LogLine(string direction, ReadOnlySpan<byte> data, double t)
    {
        string hex = Convert.ToHexString(data);
        lock (_logLock)
        {
            _log.WriteLine($"{t:F6}\t{direction}\t{hex}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        lock (_logLock)
        {
            _log.Flush();
            _log.Dispose();
        }
    }
}
