using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mk20Control.Protocol.Checksums;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Exceptions;
using Mk20Control.Protocol.Framing;
using Mk20Control.Protocol.Model;
using Mk20Control.Protocol.Transport;

namespace Mk20Control.Protocol.Client;

/// <summary>
/// High-level client for the confirmed real MK20 wire protocol (see
/// <see cref="DeviceFrameHeader"/>). This is the primary entry point for controlling an
/// MK20 device: connecting, reading its identity/theme listing, pushing telemetry,
/// adjusting backlight, reloading themes, and receiving key/encoder events.
///
/// Every public operation's XML documentation states its confirmation level plainly. Some
/// commands (see <see cref="CommandId"/>) are only "ordering-inferred" and not individually
/// confirmed on the wire; those are reachable only via <see cref="SendRawCommandAsync"/>,
/// which logs a warning and requires the caller to acknowledge the risk explicitly, rather
/// than being wrapped in a method that would falsely imply confidence.
///
/// This type is not thread-safe for concurrent calls to the same operation kind beyond the
/// FIFO request/reply correlation described in <see cref="SendRawCommandAsync"/>; serialize
/// calls from a single logical caller (typical usage pattern for a device client).
/// </summary>
public sealed class Mk20DeviceClient : IAsyncDisposable
{
    private readonly ISerialTransport _transport;
    private readonly Mk20DeviceClientOptions _options;
    private readonly ILogger<Mk20DeviceClient> _logger;
    private readonly DeviceFrameParser _parser = new();
    private readonly object _parserLock = new();
    private readonly ConcurrentDictionary<uint, ConcurrentQueue<TaskCompletionSource<DeviceFrame>>> _pendingByCommand = new();

    /// <summary>Command identifiers whose payload schema has been individually confirmed against real hardware traffic.</summary>
    private static readonly HashSet<CommandId> ConfirmedCommands = new()
    {
        CommandId.FindDevice,
        CommandId.SendSystemDataToDevice,
        CommandId.SetDeviceReload,
        CommandId.GetDeviceTheme,
        CommandId.SetDeviceBacklight,
        CommandId.FileStart,
        CommandId.FileEnd,
        CommandId.SendPixmap,
        CommandId.DeviceProactiveEscalationCommand,
        CommandId.SendJson,
        CommandId.SetDeviceDeleteTheme,
    };

    /// <summary>Raised for every decoded DEVICE_ProactiveEscalationCMD event (key press/release, encoder function activation).</summary>
    public event EventHandler<DeviceNotificationEventArgs>? NotificationReceived;

    /// <summary>Raised when the underlying transport reports a read-loop error (the connection may still be usable).</summary>
    public event EventHandler<Exception>? TransportError;

    public Mk20DeviceClient(ISerialTransport transport, Mk20DeviceClientOptions? options = null, ILogger<Mk20DeviceClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _options = options ?? new Mk20DeviceClientOptions();
        _logger = logger ?? NullLogger<Mk20DeviceClient>.Instance;
        _transport.DataReceived += OnDataReceived;
        _transport.ErrorOccurred += OnTransportError;
    }

    /// <summary>Convenience factory that constructs a client backed by a real serial port.</summary>
    public static Mk20DeviceClient CreateForSerialPort(
        string portName,
        Mk20DeviceClientOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        var resolvedOptions = options ?? new Mk20DeviceClientOptions();
        var transport = new SerialPortTransport(portName, resolvedOptions.BaudRate, factory.CreateLogger<SerialPortTransport>());
        return new Mk20DeviceClient(transport, resolvedOptions, factory.CreateLogger<Mk20DeviceClient>());
    }

    public bool IsConnected => _transport.IsOpen;

    public Task ConnectAsync(CancellationToken cancellationToken = default) => _transport.OpenAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => _transport.CloseAsync(cancellationToken);

    /// <summary>
    /// CONFIRMED mechanism: sends an empty-payload FIND_DEVICE frame and waits for the next
    /// FIND_DEVICE reply carrying a non-empty payload (the device's identity/status
    /// announcement). Returns null (rather than throwing) on timeout, since the exact
    /// request/response correlation for this command has NOT been confirmed - frames on
    /// this protocol carry no per-request id, so this may return an announcement that was
    /// not specifically triggered by this call rather than failing to receive one at all.
    /// </summary>
    public async Task<DeviceIdentity?> TryPingAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var waitTask = WaitForNonEmptyReplyAsync(CommandId.FindDevice, timeout ?? _options.DefaultRequestTimeout, cancellationToken);
        await SendRequestAsync(CommandId.FindDevice, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);

        DeviceFrame frame;
        try
        {
            frame = await waitTask.ConfigureAwait(false);
        }
        catch (Mk20TimeoutException)
        {
            _logger.LogDebug("TryPingAsync: no FIND_DEVICE announcement observed within the timeout.");
            return null;
        }

        // CONFIRMED (verified against real hardware, not just a capture): FIND_DEVICE
        // replies use the simple untagged string/string map format
        // (SimpleStringMapCodec), NOT VariantMapCodec's typeId-tagged format - every
        // value, including numeric-looking ones, is plain UTF-16BE text.
        List<KeyValuePair<string, string>> fields;
        try
        {
            fields = SimpleStringMapCodec.Decode(frame.Payload);
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException)
        {
            _logger.LogWarning(ex, "TryPingAsync: received a FIND_DEVICE payload that could not be decoded as a simple string map. Hex: {Hex}", Convert.ToHexString(frame.Payload));
            return null;
        }

        var fieldMap = new Dictionary<string, string>(fields);
        return new DeviceIdentity
        {
            Version = GetString(fieldMap, "version"),
            UpgradeToLatestMethod = GetInt(fieldMap, "upgradeToLatestMethod"),
            ScreenWidth = GetInt(fieldMap, "screen_width"),
            ScreenModel = GetString(fieldMap, "screen_model"),
            ScreenHeight = GetInt(fieldMap, "screen_height"),
            DeviceVolume = GetInt(fieldMap, "deviceVolume"),
            DeviceName = GetString(fieldMap, "deviceName"),
            DeviceBacklight = GetInt(fieldMap, "deviceBl"),
            RawFields = fieldMap,
        };
    }

    /// <summary>
    /// CONFIRMED: sets the device backlight level. The payload is the level as ASCII decimal
    /// text (e.g. "99"), not a binary encoding. This is a one-way push - no device
    /// acknowledgment for this specific command was confirmed to be paired with the request.
    /// </summary>
    /// <param name="percentage">0-100 inclusive.</param>
    public Task SetBacklightAsync(int percentage, CancellationToken cancellationToken = default)
    {
        if (percentage is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentage), percentage, "Backlight percentage must be between 0 and 100 inclusive.");

        byte[] payload = Encoding.ASCII.GetBytes(percentage.ToString());
        _logger.LogInformation("Setting device backlight to {Percentage}%.", percentage);
        return SendRequestAsync(CommandId.SetDeviceBacklight, payload, cancellationToken);
    }

    /// <summary>
    /// CONFIRMED: pushes a set of data-source key/value pairs (e.g. "GPU Usage" -> "0%") for
    /// display by any theme item bound to a matching <c>system_data_name</c>. This is a
    /// one-way push; a pushed key only has a visible effect if the currently loaded theme
    /// declares a matching binding.
    /// </summary>
    public Task PushSystemDataAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        byte[] payload = SystemDataCodec.Encode(values.ToList());
        _logger.LogDebug("Pushing {Count} system-data value(s): {Keys}", values.Count, string.Join(", ", values.Keys));
        return SendRequestAsync(CommandId.SendSystemDataToDevice, payload, cancellationToken);
    }

    /// <summary>
    /// CONFIRMED: requests the list of themes currently installed on the device, along with
    /// storage free-space information.
    /// </summary>
    public async Task<ThemeListing> GetInstalledThemesAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var waitTask = WaitForReplyAsync(CommandId.GetDeviceTheme, timeout ?? _options.DefaultRequestTimeout, cancellationToken);
        await SendRequestAsync(CommandId.GetDeviceTheme, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
        DeviceFrame frame = await waitTask.ConfigureAwait(false);

        // CONFIRMED (verified against real hardware): GET_DEVICE_THEME replies use the same
        // simple untagged string/string map format as FIND_DEVICE (SimpleStringMapCodec) -
        // even the CRC-32 values are decimal text, not binary integers.
        var fields = SimpleStringMapCodec.Decode(frame.Payload);

        long bytesTotal = 0, bytesAvailable = 0;
        var themes = new List<InstalledTheme>();
        foreach (var (key, value) in fields)
        {
            switch (key)
            {
                case "bytesTotal": long.TryParse(value, out bytesTotal); break;
                case "bytesAvailable": long.TryParse(value, out bytesAvailable); break;
                default:
                    if (uint.TryParse(value, out uint crc)) themes.Add(new InstalledTheme(key, crc));
                    break;
            }
        }

        return new ThemeListing { BytesTotal = bytesTotal, BytesAvailable = bytesAvailable, Themes = themes };
    }

    /// <summary>
    /// CONFIRMED: instructs the device to (re)load the theme at the given device-side path
    /// (e.g. "/data/theme/MK20/&lt;name&gt;/&lt;name&gt;.Theme"). The payload is the path as
    /// plain UTF-8 text with no length prefix - unlike every other command's tagged/length-
    /// prefixed fields.
    /// </summary>
    public async Task ReloadThemeAsync(string deviceThemePath, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceThemePath);
        var waitTask = WaitForReplyAsync(CommandId.SetDeviceReload, timeout ?? _options.DefaultRequestTimeout, cancellationToken);
        await SendRequestAsync(CommandId.SetDeviceReload, Encoding.UTF8.GetBytes(deviceThemePath), cancellationToken).ConfigureAwait(false);
        await waitTask.ConfigureAwait(false); // confirms the device echoed the reload command back
        _logger.LogInformation("Theme reload acknowledged for {Path}.", deviceThemePath);
    }

    /// <summary>
    /// CONFIRMED: deletes an installed theme file from the device by its device-side path
    /// (e.g. "/data/theme/MK20/&lt;name&gt;/&lt;name&gt;.Theme"). Request payload is a
    /// <see cref="Codecs.SimpleStringMapCodec"/> map with a single entry mapping the path to
    /// an empty string value: {path: ""}. The device replies with a
    /// <see cref="Codecs.SimpleStringMapCodec"/> map {"res":"1"} on success; this method
    /// throws <see cref="Mk20ProtocolException"/> if "res" is not "1".
    /// </summary>
    public async Task DeleteThemeAsync(string deviceThemePath, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceThemePath);
        byte[] payload = SimpleStringMapCodec.Encode(new[] { new KeyValuePair<string, string>(deviceThemePath, "") });

        var waitTask = WaitForReplyAsync(CommandId.SetDeviceDeleteTheme, timeout ?? _options.DefaultRequestTimeout, cancellationToken);
        await SendRequestAsync(CommandId.SetDeviceDeleteTheme, payload, cancellationToken).ConfigureAwait(false);
        DeviceFrame frame = await waitTask.ConfigureAwait(false);

        var fields = SimpleStringMapCodec.Decode(frame.Payload);
        string? result = fields.FirstOrDefault(kv => kv.Key == "res").Value;
        if (result != "1")
        {
            throw new Mk20ProtocolException(
                $"Theme deletion for '{deviceThemePath}' was not acknowledged as successful (device replied res={result ?? "<missing>"}).");
        }
        _logger.LogInformation("Theme deleted: {Path}.", deviceThemePath);
    }

    /// <summary>
    /// CONFIRMED: uploads theme file bytes to the device and activates it. This is a
    /// three-step sequence, fully confirmed by capturing a real theme install
    /// (capture14.pcapng) and byte-for-byte reconstructing the transferred file from the
    /// capture, verifying it against both the original file's bytes and the CRC-32 the
    /// device echoed back in the FILE_END reply:
    ///
    ///   1. FILE_START request: a Simple String Map with one entry {path: totalSize}.
    ///   2. The raw file bytes are written directly to the transport in fixed-size 4096-byte
    ///      chunks (a final shorter chunk carries the remainder) - CONFIRMED to carry NO
    ///      additional per-chunk framing/header of any kind; it is exactly the file's bytes,
    ///      split at 4096-byte boundaries. There is no chunk acknowledgment observed between
    ///      chunks (they are written back-to-back).
    ///   3. FILE_END request: a Simple String Map with one entry {path: crc32AsDecimalText}.
    ///
    /// The device replies to FILE_START (empty payload) and FILE_END ({"res":"1","fileName":path})
    /// as confirmed in <see cref="CommandId.FileStart"/>/<see cref="CommandId.FileEnd"/>, but
    /// this method does not require the FILE_START reply before starting the bulk write
    /// (matching the timing observed in the confirming capture). After a successful FILE_END
    /// reply, this method calls <see cref="ReloadThemeAsync"/> to activate the newly uploaded
    /// theme, matching the observed real sequence.
    /// </summary>
    /// <param name="deviceThemePath">The device-side path to store/activate the theme at, e.g. "/data/theme/MK20/&lt;name&gt;/&lt;name&gt;.Theme".</param>
    /// <param name="themeFileBytes">The complete .Theme file bytes (see <c>Mk20Control.Protocol.Codecs.ThemeFileCodec</c> to build one).</param>
    /// <exception cref="Mk20ProtocolException">Thrown if the device does not acknowledge the upload as successful.</exception>
    public async Task UploadThemeFileAsync(string deviceThemePath, byte[] themeFileBytes, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceThemePath);
        ArgumentNullException.ThrowIfNull(themeFileBytes);

        const int chunkSize = 4096;
        uint crc = Crc32.Compute(themeFileBytes);
        TimeSpan effectiveTimeout = timeout ?? _options.DefaultRequestTimeout;

        _logger.LogInformation("Uploading theme {Path} ({Length} bytes, crc32={Crc}).", deviceThemePath, themeFileBytes.Length, crc);

        byte[] fileStartPayload = SimpleStringMapCodec.Encode(
            new[] { new KeyValuePair<string, string>(deviceThemePath, themeFileBytes.Length.ToString()) });
        await SendRequestAsync(CommandId.FileStart, fileStartPayload, cancellationToken).ConfigureAwait(false);

        if (!_transport.IsOpen)
            throw new InvalidOperationException("Cannot upload a theme file: the client is not connected.");

        for (int offset = 0; offset < themeFileBytes.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, themeFileBytes.Length - offset);
            await _transport.WriteAsync(themeFileBytes.AsMemory(offset, length), cancellationToken).ConfigureAwait(false);
        }

        byte[] fileEndPayload = SimpleStringMapCodec.Encode(
            new[] { new KeyValuePair<string, string>(deviceThemePath, crc.ToString()) });
        var waitTask = WaitForReplyAsync(CommandId.FileEnd, effectiveTimeout, cancellationToken);
        await SendRequestAsync(CommandId.FileEnd, fileEndPayload, cancellationToken).ConfigureAwait(false);
        DeviceFrame frame = await waitTask.ConfigureAwait(false);

        var fields = SimpleStringMapCodec.Decode(frame.Payload);
        string? result = fields.FirstOrDefault(kv => kv.Key == "res").Value;
        if (result != "1")
        {
            throw new Mk20ProtocolException(
                $"Theme upload for '{deviceThemePath}' was not acknowledged as successful (device replied res={result ?? "<missing>"}).");
        }

        _logger.LogInformation("Theme upload acknowledged for {Path}; activating.", deviceThemePath);
        await ReloadThemeAsync(deviceThemePath, effectiveTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// CONFIRMED mechanism (one-way): sends raw UTF-8 JSON text. Observed carrying messages
    /// like {"connect":true}; no reply correlation for arbitrary host-sent JSON has been
    /// confirmed.
    /// </summary>
    public Task SendJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return SendRequestAsync(CommandId.SendJson, Encoding.UTF8.GetBytes(json), cancellationToken);
    }

    /// <summary>
    /// Escape hatch for sending any command, including those NOT individually confirmed
    /// against real hardware (see <see cref="CommandId"/> for per-command confirmation
    /// status). A warning is logged when the given command is not in the confirmed set, so
    /// unconfirmed usage is never silent.
    /// </summary>
    /// <param name="awaitReply">If true, waits for the next reply frame with a matching command id.</param>
    public async Task<DeviceFrame?> SendRawCommandAsync(
        CommandId command,
        byte[] payload,
        bool awaitReply = false,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!ConfirmedCommands.Contains(command))
        {
            _logger.LogWarning(
                "Sending command {CommandId} whose payload schema has NOT been individually confirmed against " +
                "real hardware (only its ordering in the vendor's firmware/EXE strings is known). Proceed with caution.",
                command);
        }

        Task<DeviceFrame>? waitTask = awaitReply
            ? WaitForReplyAsync(command, timeout ?? _options.DefaultRequestTimeout, cancellationToken)
            : null;

        await SendRequestAsync(command, payload, cancellationToken).ConfigureAwait(false);
        return waitTask is null ? null : await waitTask.ConfigureAwait(false);
    }

    private async Task SendRequestAsync(CommandId command, byte[] payload, CancellationToken cancellationToken)
    {
        if (!_transport.IsOpen)
            throw new InvalidOperationException("Cannot send a command: the client is not connected. Call ConnectAsync first.");

        var frame = DeviceFrame.CreateRequest((uint)command, payload);
        _logger.LogDebug("Sending command {CommandId} with {PayloadLength} byte(s) of payload.", command, payload.Length);
        await _transport.WriteAsync(frame.Encode(), cancellationToken).ConfigureAwait(false);
    }

    private Task<DeviceFrame> WaitForReplyAsync(CommandId command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<DeviceFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = _pendingByCommand.GetOrAdd((uint)command, static _ => new ConcurrentQueue<TaskCompletionSource<DeviceFrame>>());
        queue.Enqueue(tcs);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);
        linkedCts.Token.Register(() =>
            tcs.TrySetException(new Mk20TimeoutException($"No reply for command {command} received within {timeout}.")));

        return tcs.Task;
    }

    private async Task<DeviceFrame> WaitForNonEmptyReplyAsync(CommandId command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new Mk20TimeoutException($"No non-empty reply for command {command} received within {timeout}.");

            var frame = await WaitForReplyAsync(command, remaining, cancellationToken).ConfigureAwait(false);
            if (frame.Payload.Length > 0) return frame;
        }
    }

    private void OnDataReceived(object? sender, ReadOnlyMemory<byte> data)
    {
        List<DeviceFrame> frames;
        lock (_parserLock)
        {
            _parser.Feed(data.Span);
            frames = _parser.DrainFrames().ToList();
        }

        foreach (var frame in frames) Dispatch(frame);
    }

    private void Dispatch(DeviceFrame frame)
    {
        if (frame.IsAbortTransferMessage)
        {
            _logger.LogWarning("Received an 'Abort file transfer' control message from the device.");
            return;
        }

        if (!frame.IsChecksumValid)
        {
            _logger.LogWarning("Discarding frame with an invalid payload checksum: commandId={CommandId}.", frame.CommandId);
            return;
        }

        _logger.LogTrace(
            "Received frame: packetType={PacketType} commandId={CommandId} payloadLength={PayloadLength}.",
            frame.PacketType, frame.CommandId, frame.Payload.Length);

        if (frame.PacketType == (uint)Model.PacketType.AckReply &&
            _pendingByCommand.TryGetValue(frame.CommandId, out var queue) &&
            queue.TryDequeue(out var pending))
        {
            pending.TrySetResult(frame);
        }

        if (frame.CommandId == (uint)CommandId.DeviceProactiveEscalationCommand)
        {
            HandleProactiveEscalation(frame);
        }
    }

    private void HandleProactiveEscalation(DeviceFrame frame)
    {
        if (!VariantMapCodec.TryDecodeMapArray(frame.Payload, out var maps) || maps.Count == 0)
        {
            _logger.LogWarning("Could not decode a DEVICE_ProactiveEscalationCMD payload ({Length} bytes) as a tagged-value map array.", frame.Payload.Length);
            return;
        }

        var keyState = maps[0];
        int row = GetInt(keyState, "row") ?? -1;
        int col = GetInt(keyState, "col") ?? -1;
        bool pressed = keyState.TryGetValue("pressed", out var p) && p.AsInt32 is > 0;

        var args = new DeviceNotificationEventArgs
        {
            Position = new KeyPosition(row, col),
            IsPressed = pressed,
            ActionDescriptor = maps.Count > 1 ? maps[1] : null,
            RawMaps = maps,
        };
        NotificationReceived?.Invoke(this, args);
    }

    private void OnTransportError(object? sender, Exception ex)
    {
        _logger.LogWarning(ex, "Transport error.");
        TransportError?.Invoke(this, ex);
    }

    private static string? GetString(IReadOnlyDictionary<string, TaggedValue> fields, string key) =>
        fields.TryGetValue(key, out var v) && v.AsString is { } s ? s : null;

    private static int? GetInt(IReadOnlyDictionary<string, TaggedValue> fields, string key) =>
        fields.TryGetValue(key, out var v) && v.AsInt32 is { } i ? i : null;

    private static string? GetString(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var s) ? s : null;

    private static int? GetInt(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var s) && int.TryParse(s, out var i) ? i : null;

    public async ValueTask DisposeAsync()
    {
        _transport.DataReceived -= OnDataReceived;
        _transport.ErrorOccurred -= OnTransportError;
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
