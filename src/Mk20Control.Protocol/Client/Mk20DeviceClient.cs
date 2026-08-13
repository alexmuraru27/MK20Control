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
using Mk20Control.Protocol.Theme;
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
///
/// Operation safeguards (added after a confirmed real-hardware freeze - see
/// PROTOCOL_WAVESHARE_MK20.md §10 Open Item #8): <see cref="ReloadThemeAsync"/>,
/// <see cref="DeleteThemeAsync"/>, and <see cref="UploadThemeFileAsync"/> are automatically
/// serialized against each other (a second call simply waits for the first to fully finish
/// rather than racing on the wire), and <see cref="DeleteThemeAsync"/> refuses to delete a
/// theme whose reload was sent but never confirmed acknowledged - see
/// <see cref="IsReloadPending"/>/<see cref="ClearPendingReloadState"/>.
/// </summary>
public sealed class Mk20DeviceClient : IAsyncDisposable
{
    private readonly ISerialTransport _transport;
    private readonly Mk20DeviceClientOptions _options;
    private readonly ILogger<Mk20DeviceClient> _logger;
    private readonly DeviceFrameParser _parser = new();
    private readonly object _parserLock = new();
    private readonly ConcurrentDictionary<uint, ConcurrentQueue<TaskCompletionSource<DeviceFrame>>> _pendingByCommand = new();

    /// <summary>
    /// Serializes every theme-mutating operation (<see cref="ReloadThemeAsync"/>,
    /// <see cref="DeleteThemeAsync"/>, <see cref="UploadThemeFileAsync"/>) so this client
    /// never has more than one such operation in flight at a time, even if a caller invokes
    /// them concurrently without awaiting - this is the "don't spam the device with
    /// operations" safeguard: a second call simply waits for the first to fully finish
    /// (success, protocol error, or timeout) before starting, matching how the real
    /// ScreenKeyWindows host appears to behave (every real capture examined shows exactly
    /// one theme operation in flight at a time; see PROTOCOL_WAVESHARE_MK20.md §10).
    /// </summary>
    private readonly SemaphoreSlim _themeOperationLock = new(1, 1);

    /// <summary>
    /// Device-side theme paths whose <see cref="ReloadThemeAsync"/> (or the equivalent
    /// reload phase inside <see cref="UploadThemeFileAsync"/>) was sent but not yet confirmed
    /// acknowledged - "not yet confirmed" includes a call that timed out, since a client-side
    /// timeout does NOT prove the device gave up (it may still be processing). This is the
    /// "wait for the device to ack, don't delete out from under it" safeguard: see
    /// <see cref="DeleteThemeAsync"/>, which refuses to delete a path while it's in this set.
    /// A path is removed only once its reload is positively confirmed acknowledged.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _pendingReloadPaths = new();

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

    /// <summary>
    /// Raised for every unsolicited <c>SEND_JSON</c> (command 15) frame the device pushes -
    /// the argument is the raw UTF-8 JSON text. The device uses this to report its own state,
    /// e.g. which images/system data it wants, and - confirmed via a real nested-folder
    /// navigation capture - a <c>"themePageSwitch": true</c> field on every page change,
    /// whether that change came from a relative page-switch key, an absolute
    /// <c>jumpToPage</c>, entering a folder (<c>openPage</c>) or leaving one
    /// (<c>oneLevelUp</c>). See also <see cref="PageSwitched"/>.
    /// </summary>
    public event EventHandler<string>? JsonReceived;

    /// <summary>
    /// Raised whenever the device reports that its active page changed (a <c>SEND_JSON</c>
    /// frame carrying <c>"themePageSwitch": true</c>) - the device's only confirmation that a
    /// navigation key actually did something, which makes it the reliable way to verify
    /// page/folder navigation on real hardware.
    /// </summary>
    public event EventHandler? PageSwitched;

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
        await _themeOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _pendingReloadPaths[deviceThemePath] = 0;
            var waitTask = WaitForReplyAsync(CommandId.SetDeviceReload, timeout ?? _options.DefaultRequestTimeout, cancellationToken);
            // NOTE: unlike the upload pipeline (UploadThemeFileAsync, which always precedes
            // SET_DEVICE_RELOAD with an abort-transfer immediately after FILE_END), a *standalone*
            // reload of an already-installed theme (no re-upload) was observed in the confirming
            // capture WITHOUT any preceding abort-transfer message - so this method intentionally
            // does not send one, matching that real-world sequencing exactly.
            await SendRequestAsync(CommandId.SetDeviceReload, Encoding.UTF8.GetBytes(deviceThemePath), cancellationToken).ConfigureAwait(false);
            await waitTask.ConfigureAwait(false); // confirms the device echoed the reload command back
            _pendingReloadPaths.TryRemove(deviceThemePath, out _);
            _logger.LogInformation("Theme reload acknowledged for {Path}.", deviceThemePath);
        }
        finally
        {
            _themeOperationLock.Release();
        }
    }

    /// <summary>
    /// True if <paramref name="deviceThemePath"/> has a reload that was sent but never
    /// confirmed acknowledged (including one that timed out) - see
    /// <see cref="_pendingReloadPaths"/> remarks. <see cref="DeleteThemeAsync"/> refuses to
    /// delete such a path; call <see cref="ClearPendingReloadState"/> to override once you
    /// have independently confirmed it is safe to do so (e.g. after power-cycling the device).
    /// </summary>
    public bool IsReloadPending(string deviceThemePath) => _pendingReloadPaths.ContainsKey(deviceThemePath);

    /// <summary>
    /// Clears the "pending reload" safeguard tracked for <paramref name="deviceThemePath"/>
    /// (or every tracked path, if null) - see <see cref="_pendingReloadPaths"/> remarks. Only
    /// call this once you have independently confirmed it's safe (e.g. the device was just
    /// power-cycled, or a subsequent successful reload/ping proves it recovered) - this
    /// safeguard exists specifically because a confirmed real-hardware hazard was observed
    /// when it was bypassed (see PROTOCOL_WAVESHARE_MK20.md §10 Open Item #8).
    /// </summary>
    public void ClearPendingReloadState(string? deviceThemePath = null)
    {
        if (deviceThemePath is null) { _pendingReloadPaths.Clear(); return; }
        _pendingReloadPaths.TryRemove(deviceThemePath, out _);
    }

    /// <summary>
    /// CONFIRMED: deletes an installed theme file from the device by its device-side path
    /// (e.g. "/data/theme/MK20/&lt;name&gt;/&lt;name&gt;.Theme"). Request payload is a
    /// <see cref="Codecs.SimpleStringMapCodec"/> map with a single entry mapping the path to
    /// an empty string value: {path: ""}. The device replies with a
    /// <see cref="Codecs.SimpleStringMapCodec"/> map {"res":"1"} on success; this method
    /// throws <see cref="Mk20ProtocolException"/> if "res" is not "1".
    ///
    /// SAFEGUARD (confirmed hazard on real hardware): this method throws
    /// <see cref="InvalidOperationException"/> up front, before sending anything, if
    /// <paramref name="deviceThemePath"/> has a reload that was sent but never confirmed
    /// acknowledged (including one that timed out) - see <see cref="IsReloadPending"/>.
    /// Deleting a theme while its reload may still be in flight was observed to leave the
    /// device's render/reload subsystem stuck (it keeps responding normally to
    /// <see cref="TryPingAsync"/>/<see cref="GetInstalledThemesAsync"/>, but every subsequent
    /// <see cref="ReloadThemeAsync"/> call - even for a different, previously-working theme -
    /// stops being acknowledged, and the physical display appears frozen). Only a physical
    /// power-cycle was confirmed to recover from that state; see
    /// PROTOCOL_WAVESHARE_MK20.md §10 Open Item #8. Call
    /// <see cref="ClearPendingReloadState"/> first if you have independently confirmed it's
    /// safe to proceed anyway.
    /// </summary>
    public async Task DeleteThemeAsync(string deviceThemePath, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceThemePath);
        if (_pendingReloadPaths.ContainsKey(deviceThemePath))
        {
            throw new InvalidOperationException(
                $"Refusing to delete '{deviceThemePath}': its SET_DEVICE_RELOAD was sent but never confirmed " +
                "acknowledged (this includes a call that timed out - a client-side timeout does not prove the " +
                "device gave up on it). Deleting a theme while its reload may still be in flight was confirmed " +
                "on real hardware to leave the device's render subsystem stuck, requiring a physical power-cycle " +
                "to recover. Call ClearPendingReloadState(deviceThemePath) first if you have independently " +
                "confirmed it's safe to proceed (e.g. the device was just power-cycled).");
        }

        await _themeOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
        finally
        {
            _themeOperationLock.Release();
        }
    }

    /// <summary>
    /// CONFIRMED: uploads theme file bytes to the device and activates it. This is a
    /// three-step sequence, fully confirmed by capturing real theme installs and
    /// byte-for-byte reconstructing the transferred file from the capture, verifying it
    /// against both the original file's bytes and the CRC-32 the device echoed back in the
    /// FILE_END reply:
    ///
    ///   1. FILE_START request: a Simple String Map with one entry {path: totalSize}, preceded
    ///      by an "Abort file transfer" control message.
    ///   2. The raw file bytes are written directly to the transport in fixed-size 4096-byte
    ///      chunks (a final shorter chunk carries the remainder) - CONFIRMED to carry NO
    ///      additional per-chunk framing/header of any kind; it is exactly the file's bytes,
    ///      split at 4096-byte boundaries. There is no chunk acknowledgment observed between
    ///      chunks (they are written back-to-back).
    ///   3. FILE_END request: a Simple String Map with one entry {path: crc32AsDecimalText}.
    ///   4. Once FILE_END's reply confirms the write succeeded: another "Abort file transfer"
    ///      control message, then a SET_DEVICE_RELOAD request for the same path.
    ///
    /// The device replies to FILE_START (empty payload) and FILE_END ({"res":"1","fileName":path})
    /// as confirmed in <see cref="CommandId.FileStart"/>/<see cref="CommandId.FileEnd"/>, but
    /// this method does not require the FILE_START reply before starting the bulk write
    /// (matching the timing observed in the confirming capture).
    /// </summary>
    /// <param name="deviceThemePath">The device-side path to store/activate the theme at, e.g. "/data/theme/MK20/&lt;name&gt;/&lt;name&gt;.Theme".</param>
    /// <param name="themeFileBytes">The complete .Theme file bytes (see <c>Mk20Control.Protocol.Codecs.ThemeFileCodec</c> to build one).</param>
    /// <exception cref="Mk20ProtocolException">Thrown if the device does not acknowledge the upload as successful.</exception>
    public async Task UploadThemeFileAsync(string deviceThemePath, byte[] themeFileBytes, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceThemePath);
        ArgumentNullException.ThrowIfNull(themeFileBytes);

        // The device resumes on whichever page the layout JSON's "main.currentPage" names -
        // this is NOT necessarily the first page in the "pages" array (e.g. it drifts to
        // whatever page was last open when the file was re-saved by ScreenKeyWindows, or
        // whatever ThemeEditor last had selected). Force it back to the first page here so
        // every upload through this client always activates showing page 1, regardless of
        // what value happened to be embedded in the bytes handed to this method.
        themeFileBytes = NormalizeToFirstPage(themeFileBytes);

        if (_pendingReloadPaths.ContainsKey(deviceThemePath))
        {
            _logger.LogWarning(
                "Re-uploading '{Path}' while its previous SET_DEVICE_RELOAD is still unconfirmed - proceeding " +
                "(unlike DeleteThemeAsync, re-uploading/re-activating the same theme is not known to be hazardous), " +
                "but if the device is unresponsive consider verifying/power-cycling first.", deviceThemePath);
        }

        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await UploadThemeFileAttemptAsync(deviceThemePath, themeFileBytes, timeout, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Mk20TimeoutException ex) when (attempt < maxAttempts)
            {
                // Confirmed on real hardware (PROTOCOL_WAVESHARE_MK20.md §10 Open Item #9):
                // FILE_END/SET_DEVICE_RELOAD occasionally never gets acknowledged even for a
                // byte-identical transfer that succeeds moments later - a low-probability,
                // non-deterministic device-firmware condition, not tied to file size or
                // content. A same-session retry (without power-cycling) has been directly
                // confirmed NOT to help once the device's whole command processor has locked
                // up (FIND_DEVICE itself stops replying), so before retrying we first confirm
                // the device is still alive; if it isn't, we fail fast with a clear message
                // instead of silently retrying against a dead link.
                _logger.LogWarning(ex, "Upload attempt {Attempt}/{Max} for {Path} timed out; checking device health before retrying.", attempt, maxAttempts, deviceThemePath);
                DeviceIdentity? identity = await TryPingAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                if (identity is null)
                {
                    throw new Mk20TimeoutException(
                        $"Upload for '{deviceThemePath}' timed out and the device is no longer responding to FIND_DEVICE " +
                        "(the whole command processor appears locked up, not just the reload path) - a physical power-cycle " +
                        "is required before any further attempt will succeed.");
                }
                _pendingReloadPaths.TryRemove(deviceThemePath, out _);
            }
        }
    }

    /// <summary>
    /// Re-encodes <paramref name="themeFileBytes"/> with "main.currentPage" set to its first
    /// page's id, if it isn't already - so activating this theme always opens on page 1. A
    /// no-op (returns the original bytes unchanged) for single-page themes or themes whose
    /// currentPage already matches the first page, to avoid needless re-encoding.
    /// </summary>
    private static byte[] NormalizeToFirstPage(byte[] themeFileBytes)
    {
        ThemeFile theme;
        try
        {
            theme = ThemeFileCodec.Decode(themeFileBytes);
        }
        catch
        {
            // Not a well-formed .Theme file this codec understands - upload as-is rather than
            // failing the whole operation over a best-effort convenience tweak.
            return themeFileBytes;
        }

        if (theme.Pages.Count == 0) return themeFileBytes;
        string firstPageId = theme.Pages[0].PageName ?? "";
        if (theme.CurrentPageId == firstPageId) return themeFileBytes;

        var normalized = theme with { CurrentPageId = firstPageId };
        return ThemeFileCodec.Encode(normalized);
    }

    private async Task UploadThemeFileAttemptAsync(string deviceThemePath, byte[] themeFileBytes, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        await _themeOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            const int chunkSize = 4096;
            uint crc = Crc32.Compute(themeFileBytes);
            TimeSpan effectiveTimeout = timeout ?? _options.DefaultRequestTimeout;

            _logger.LogInformation("Uploading theme {Path} ({Length} bytes, crc32={Crc}).", deviceThemePath, themeFileBytes.Length, crc);

            byte[] fileStartPayload = SimpleStringMapCodec.Encode(
                new[] { new KeyValuePair<string, string>(deviceThemePath, themeFileBytes.Length.ToString()) });
            // Confirmed in every real capture examined (capture, capture11, capture14,
            // capture15, capture16 - 5/5): the host sends GET_DEVICE_THEME immediately before
            // the abort+FILE_START pair that starts an upload.
            await GetInstalledThemesAsync(effectiveTimeout, cancellationToken).ConfigureAwait(false);
            // The host sends one "Abort file transfer" control message immediately before
            // FILE_START, resetting the device's file-transfer state machine before a new
            // upload. See SendAbortFileTransferAsync remarks.
            await SendAbortFileTransferAsync(cancellationToken).ConfigureAwait(false);
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

            var fileEndWaitTask = WaitForReplyAsync(CommandId.FileEnd, effectiveTimeout, cancellationToken);
            await SendRequestAsync(CommandId.FileEnd, fileEndPayload, cancellationToken).ConfigureAwait(false);
            DeviceFrame frame = await fileEndWaitTask.ConfigureAwait(false);

            var fields = SimpleStringMapCodec.Decode(frame.Payload);
            string? result = fields.FirstOrDefault(kv => kv.Key == "res").Value;
            if (result != "1")
            {
                throw new Mk20ProtocolException(
                    $"Theme upload for '{deviceThemePath}' was not acknowledged as successful (device replied res={result ?? "<missing>"}).");
            }

            _logger.LogInformation("Theme upload acknowledged for {Path}; activating.", deviceThemePath);

            // The device replies to FILE_END before the file is actually reloadable, so only
            // send SET_DEVICE_RELOAD once FILE_END's reply confirms the write succeeded -
            // preceded by the same abort-transfer control message sent before FILE_START.
            _pendingReloadPaths[deviceThemePath] = 0;
            var reloadWaitTask = WaitForReplyAsync(CommandId.SetDeviceReload, effectiveTimeout, cancellationToken);
            await SendAbortFileTransferAsync(cancellationToken).ConfigureAwait(false);
            await SendRequestAsync(CommandId.SetDeviceReload, Encoding.UTF8.GetBytes(deviceThemePath), cancellationToken).ConfigureAwait(false);
            await reloadWaitTask.ConfigureAwait(false); // confirms the device echoed the reload command back
            _pendingReloadPaths.TryRemove(deviceThemePath, out _);
            _logger.LogInformation("Theme reload acknowledged for {Path}.", deviceThemePath);
        }
        finally
        {
            _themeOperationLock.Release();
        }
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

    /// <summary>
    /// CONFIRMED: sends the standalone "Abort file transfer" control message (the fixed
    /// ASCII literal <see cref="DeviceFrameHeader.AbortTransferText"/>, no length prefix or
    /// checksum - not a normal command frame). The real host sends exactly one of these
    /// immediately before every <see cref="CommandId.FileStart"/> and every
    /// <see cref="CommandId.SetDeviceReload"/> request, resetting the device's
    /// file-transfer/reload state machine before starting a new operation. The device never
    /// acknowledges this message (it carries no reply), so this method returns as soon as
    /// the bytes are written.
    /// </summary>
    public async Task SendAbortFileTransferAsync(CancellationToken cancellationToken = default)
    {
        if (!_transport.IsOpen)
            throw new InvalidOperationException("Cannot send a command: the client is not connected. Call ConnectAsync first.");

        _logger.LogDebug("Sending 'Abort file transfer' control message.");
        await _transport.WriteAsync(DeviceFrameHeader.AbortTransferBytes, cancellationToken).ConfigureAwait(false);
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
            _pendingByCommand.TryGetValue(frame.CommandId, out var queue))
        {
            // Skip any already-completed waiters (e.g. ones that already timed out) rather
            // than stopping at the first entry - a timed-out TaskCompletionSource left in the
            // queue would otherwise silently "consume" this reply and starve every subsequent
            // waiter for the same command, which is exactly the bug a retry-after-timeout
            // (see UploadThemeFileAsync) would trigger.
            while (queue.TryDequeue(out var pending))
            {
                if (pending.TrySetResult(frame)) break;
            }
        }

        if (frame.CommandId == (uint)CommandId.DeviceProactiveEscalationCommand)
        {
            HandleProactiveEscalation(frame);
        }
        else if (frame.CommandId == (uint)CommandId.SendJson && frame.Payload.Length > 0)
        {
            HandleDeviceJson(frame);
        }
    }

    private void HandleDeviceJson(DeviceFrame frame)
    {
        string json;
        try
        {
            json = Encoding.UTF8.GetString(frame.Payload);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Could not decode a SEND_JSON payload ({Length} bytes) as UTF-8.", frame.Payload.Length);
            return;
        }

        JsonReceived?.Invoke(this, json);

        // Confirmed via a real nested-folder navigation capture: the device appends
        // "themePageSwitch": true to its status JSON on every page change.
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("themePageSwitch", out var switched) &&
                switched.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                _logger.LogDebug("Device reported a theme page switch.");
                PageSwitched?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON we understand - JsonReceived subscribers still saw the raw text.
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
