using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mk20Control.Protocol.Transport;

/// <summary>
/// <see cref="ISerialTransport"/> implementation backed by <see cref="System.IO.Ports.SerialPort"/>,
/// matching the confirmed real-hardware settings: 115200 baud, 8 data bits, no parity, one
/// stop bit, no flow control (CDC-ACM over USB).
/// </summary>
public sealed class SerialPortTransport : ISerialTransport
{
    private readonly SerialPort _port;
    private readonly ILogger<SerialPortTransport> _logger;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;
    private volatile bool _disposed;

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<Exception>? ErrorOccurred;

    /// <param name="portName">The OS serial port name (e.g. "COM5").</param>
    /// <param name="baudRate">Line-rate setting; over CDC-ACM this is typically a no-op for real throughput, but 115200 matches the vendor app.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger if not supplied.</param>
    public SerialPortTransport(string portName, int baudRate = 115200, ILogger<SerialPortTransport>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        _logger = logger ?? NullLogger<SerialPortTransport>.Instance;
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 2000,
            WriteTimeout = 2000,
        };
    }

    public bool IsOpen => _port.IsOpen;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_port.IsOpen) return Task.CompletedTask;

        _logger.LogInformation("Opening serial port {PortName} at {BaudRate} baud (8N1, no flow control).", _port.PortName, _port.BaudRate);
        _port.Open();

        _readLoopCts = new CancellationTokenSource();
        _readLoopTask = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen) return;

        _logger.LogInformation("Closing serial port {PortName}.", _port.PortName);
        _readLoopCts?.Cancel();
        if (_readLoopTask is not null)
        {
            try { await _readLoopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
        _port.Close();
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_port.IsOpen)
            throw new InvalidOperationException("Cannot write: the serial port is not open. Call OpenAsync first.");

        var array = data.ToArray();
        _logger.LogDebug("Writing {ByteCount} byte(s) to {PortName}.", array.Length, _port.PortName);
        _port.Write(array, 0, array.Length);
        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_port.IsOpen) { await Task.Delay(50, cancellationToken).ConfigureAwait(false); continue; }

                int available = _port.BytesToRead;
                if (available == 0) { await Task.Delay(10, cancellationToken).ConfigureAwait(false); continue; }

                int n = _port.Read(buffer, 0, Math.Min(buffer.Length, available));
                if (n > 0) DataReceived?.Invoke(this, buffer.AsMemory(0, n));
            }
            catch (TimeoutException)
            {
                // Expected on a quiet line with ReadTimeout set; not an error.
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in serial read loop for {PortName}; will retry.", _port.PortName);
                ErrorOccurred?.Invoke(this, ex);
                try { await Task.Delay(200, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await CloseAsync().ConfigureAwait(false);
        _readLoopCts?.Dispose();
        _port.Dispose();
    }
}
