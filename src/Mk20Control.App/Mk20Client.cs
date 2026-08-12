using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mk20Control.Protocol;

namespace Mk20Control.App;

/// <summary>
/// Minimal Layer-B client for the Waveshare MK10/MK20, built strictly from
/// PROTOCOL_WAVESHARE_MK20.md. This is a sandbox/experimentation client, not production code:
/// several protocol details (Layer-A cmd numbers, achievable JPEG fps, etc.) are marked
/// UNVERIFIED in the doc and should be confirmed against real hardware.
/// </summary>
public sealed class Mk20Client : IDisposable
{
    private readonly SerialPort _port;
    private readonly Mk20FrameParser _parser = new();
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<Mk20Frame>> _pending = new();
    private readonly ConcurrentQueue<Mk20Frame> _unsolicited = new();
    private CancellationTokenSource? _readLoopCts;
    private uint _nextId = 1;

    public event Action<KeyStateChanged>? KeyStateChanged;
    public event Action<string>? Log;

    public Mk20Client(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 2000,
            WriteTimeout = 2000,
        };
    }

    public bool IsOpen => _port.IsOpen;

    public void Open()
    {
        _port.Open();
        _readLoopCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_readLoopCts.Token));
        Log?.Invoke($"Opened {_port.PortName} @ {_port.BaudRate} 8N1.");
    }

    public void Close()
    {
        _readLoopCts?.Cancel();
        if (_port.IsOpen) _port.Close();
    }

    public void Dispose()
    {
        Close();
        _port.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_port.IsOpen) { await Task.Delay(50, ct); continue; }
                int n = _port.BytesToRead > 0
                    ? _port.Read(buffer, 0, Math.Min(buffer.Length, _port.BytesToRead))
                    : 0;
                if (n == 0) { await Task.Delay(10, ct); continue; }

                _parser.Feed(buffer.AsSpan(0, n));
                foreach (var frame in _parser.DrainFrames())
                {
                    Dispatch(frame);
                }
            }
            catch (TimeoutException) { /* expected on quiet lines */ }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log?.Invoke($"[read-loop] {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(200, ct);
            }
        }
    }

    private void Dispatch(Mk20Frame frame)
    {
        if (_pending.TryRemove(frame.Id, out var tcs))
        {
            tcs.TrySetResult(frame);
            return;
        }

        // Unsolicited (id doesn't match a pending request), e.g. keyStateChanged, echoes.
        _unsolicited.Enqueue(frame);
        TryParseUnsolicited(frame);
    }

    private void TryParseUnsolicited(Mk20Frame frame)
    {
        if (frame.Cmd != CmdValue.Json) return;
        try
        {
            var reply = JsonSerializer.Deserialize<JsonRpcReply>(frame.Payload);
            if (reply?.Method == "keyStateChanged" && reply.Parameters is { } p)
            {
                var evt = p.Deserialize<KeyStateChanged>();
                if (evt is not null) KeyStateChanged?.Invoke(evt);
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. a SHOW_JPG echo) - ignore here, caller handles via SendJpegAndWaitEcho.
        }
    }

    private uint NextId() => _nextId++;

    public Task<Mk20Frame> SendFrameAsync(uint cmd, byte[] payload, TimeSpan? timeout = null)
    {
        uint id = NextId();
        var tcs = new TaskCompletionSource<Mk20Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var frame = new Mk20Frame(id, cmd, payload);
        var encoded = frame.Encode();
        _port.Write(encoded, 0, encoded.Length);

        var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        cts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var t))
                t.TrySetException(new TimeoutException($"No reply for cmd={cmd} id={id} within timeout."));
        });

        return tcs.Task;
    }

    public async Task<JsonRpcReply> SendJsonAsync(string method, object? parameters = null, TimeSpan? timeout = null)
    {
        var req = new JsonRpcRequest { Method = method, Parameters = parameters };
        var payload = JsonSerializer.SerializeToUtf8Bytes(req);
        var frame = await SendFrameAsync(CmdValue.Json, payload, timeout);
        return JsonSerializer.Deserialize<JsonRpcReply>(frame.Payload)
               ?? throw new InvalidOperationException("Empty/invalid JSON reply.");
    }

    public async Task<DeviceInfo> GetInfoAsync()
    {
        var reply = await SendJsonAsync("getInfo");
        if (reply.Result is not { } result)
            throw new InvalidOperationException($"getInfo failed: {reply.ErrorString}");
        return result.Deserialize<DeviceInfo>() ?? throw new InvalidOperationException("Could not parse DeviceInfo.");
    }

    public Task<JsonRpcReply> SetBacklightAsync(int level) => SendJsonAsync("setBacklight", new { level });

    public Task<JsonRpcReply> SetVolumeAsync(int level) => SendJsonAsync("setVolume", new { level });

    public Task<JsonRpcReply> PlayAudioAsync(string filePath) => SendJsonAsync("playAudio", new { filePath });

    public Task<JsonRpcReply> StopAudioAsync() => SendJsonAsync("stopAudio");

    /// <summary>
    /// Sends a whole-canvas JPEG (Layer B, cmd 100). Per the doc, the device echoes a
    /// SHOW_JPG frame back once it's done rendering - that echo is built-in flow control
    /// and a free frame-rate meter for the self-clocked B1 loop.
    /// </summary>
    public async Task<TimeSpan> SendJpegAndWaitEchoAsync(byte[] jpegBytes, TimeSpan? timeout = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await SendFrameAsync(CmdValue.ShowJpg, jpegBytes, timeout);
        sw.Stop();
        return sw.Elapsed;
    }

    /// <summary>Layer-A style telemetry push described in doc section 7.3 - UNVERIFIED numeric cmd.</summary>
    public Task<JsonRpcReply> PushSystemDataAsync(Dictionary<string, string> systemData) =>
        SendJsonAsync("sendSystemData", systemData);
}
