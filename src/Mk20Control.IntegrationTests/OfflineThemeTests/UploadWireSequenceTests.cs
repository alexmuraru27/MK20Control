using System.Text;
using Mk20Control.Protocol.Client;
using Mk20Control.Protocol.Codecs;
using Mk20Control.Protocol.Framing;
using Mk20Control.Protocol.Model;
using Mk20Control.Protocol.Transport;
using NUnit.Framework;

namespace Mk20Control.IntegrationTests.OfflineThemeTests;

/// <summary>
/// Pins the host-side message sequence of a theme upload to what the vendor's own software
/// does on the wire, as decoded from tools/Captures (capture15, capture16, capture17,
/// capture20_bg_gif, capture22_text_input - identical in all five):
///
///   GET_DEVICE_THEME -> ABORT -> FILE_START -> raw 4096B chunks -> FILE_END -> ABORT
///   -> (device acknowledges FILE_END) -> SET_DEVICE_RELOAD
///
/// The two critical details are:
///   * the ABORT immediately after FILE_END - the device stays in file-receive mode until
///     that control message closes the bulk stream, and only then processes FILE_END; and
///   * waiting for the FILE_START reply before writing any bulk bytes - the device is not
///     counting payload until it has opened the file, so bytes written earlier are lost and
///     its counter never reaches totalSize.
///
/// <see cref="FakeDeviceTransport"/> reproduces both behaviours (including a deliberately
/// delayed FILE_START ack), so a client that gets either wrong fails here exactly the way it
/// does on real hardware.
/// </summary>
public class UploadWireSequenceTests
{
    private const string ThemeName = "seqtest";
    private static readonly string DeviceThemeFilePath = DeviceThemePath.ForTheme(ThemeName);

    /// <summary>
    /// Minimal stand-in for the device: records every host write as a decoded message name and
    /// replies the way the real firmware does, including only acting on FILE_END once the
    /// abort-transfer sentinel has arrived.
    /// </summary>
    private sealed class FakeDeviceTransport : ISerialTransport
    {
        private readonly List<byte> _inbound = new();
        private readonly object _gate = new();
        private int _fileRemaining;
        private bool _awaitingFileStartAck;
        private byte[]? _pendingFileEndPayload;

        public List<string> Sequence { get; } = new();
        public int RawFileBytes { get; private set; }

        public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
        public event EventHandler<Exception>? ErrorOccurred;

        public bool IsOpen { get; private set; }
        public Task OpenAsync(CancellationToken cancellationToken = default) { IsOpen = true; return Task.CompletedTask; }
        public Task CloseAsync(CancellationToken cancellationToken = default) { IsOpen = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            List<Action> replies = new();
            lock (_gate)
            {
                if (_awaitingFileStartAck && !Sequence.Contains("BULK-BEFORE-FILE_START-ACK"))
                {
                    Sequence.Add("BULK-BEFORE-FILE_START-ACK");
                }

                _inbound.AddRange(data.ToArray());
                Consume(replies);
            }

            // Raised outside the lock: the client completes its awaiting task on this thread.
            foreach (var reply in replies) reply();
            return Task.CompletedTask;
        }

        private void Consume(List<Action> replies)
        {
            while (true)
            {
                // While a file is in flight the device consumes exactly the byte count that
                // FILE_START declared as raw payload; framing only resumes afterwards.
                if (_fileRemaining > 0)
                {
                    int take = Math.Min(_fileRemaining, _inbound.Count);
                    if (take == 0) return;
                    _inbound.RemoveRange(0, take);
                    RawFileBytes += take;
                    _fileRemaining -= take;
                    continue;
                }

                byte[] buffer = _inbound.ToArray();
                int abort = IndexOf(buffer, DeviceFrameHeader.AbortTransferBytes);
                int magic = IndexOf(buffer, Encoding.ASCII.GetBytes(DeviceFrameHeader.CommandHeaderText));

                if (abort >= 0 && (magic < 0 || abort < magic))
                {
                    _inbound.RemoveRange(0, abort + DeviceFrameHeader.AbortTransferBytes.Length);
                    Sequence.Add("ABORT");

                    // The abort is what closes the bulk stream, so this is the point at which
                    // the device finally acts on a FILE_END it has already received.
                    if (_pendingFileEndPayload is not null)
                    {
                        byte[] payload = _pendingFileEndPayload;
                        _pendingFileEndPayload = null;
                        replies.Add(() => Reply(CommandId.FileEnd, payload));
                    }
                    continue;
                }

                if (magic < 0) return;

                int header = magic + DeviceFrameHeader.CommandHeaderText.Length;
                if (header + 16 > buffer.Length) return;
                uint command = BitConverter.ToUInt32(buffer, header + 4);
                int length = BitConverter.ToInt32(buffer, header + 8);
                if (header + 16 + length > buffer.Length) return;

                byte[] body = buffer.AsSpan(header + 16, length).ToArray();
                _inbound.RemoveRange(0, header + 16 + length);
                HandleCommand((CommandId)command, body, replies);
            }
        }

        private void HandleCommand(CommandId command, byte[] body, List<Action> replies)
        {
            Sequence.Add(command.ToString());
            switch (command)
            {
                case CommandId.GetDeviceTheme:
                    replies.Add(() => Reply(CommandId.GetDeviceTheme, SimpleStringMapCodec.Encode(new[]
                    {
                        new KeyValuePair<string, string>("bytesTotal", "28003"),
                        new KeyValuePair<string, string>("bytesAvailable", "27000"),
                    })));
                    break;

                case CommandId.FileStart:
                    // {path: totalSize} - the size tells the device how many raw bytes follow.
                    var startFields = SimpleStringMapCodec.Decode(body);
                    int total = int.Parse(startFields[0].Value);
                    _awaitingFileStartAck = true;
                    // Deliberately delayed: a real device takes a moment to open the file, and
                    // is not counting payload until it has. A host that starts writing bulk
                    // bytes without awaiting this reply loses them.
                    replies.Add(() => Task.Run(async () =>
                    {
                        await Task.Delay(50).ConfigureAwait(false);
                        lock (_gate) { _fileRemaining = total; _awaitingFileStartAck = false; }
                        Reply(CommandId.FileStart, Array.Empty<byte>());
                    }));
                    break;

                case CommandId.FileEnd:
                    // Queued, not answered: the real device only gets here after the abort.
                    _pendingFileEndPayload = SimpleStringMapCodec.Encode(new[]
                    {
                        new KeyValuePair<string, string>("res", "1"),
                        new KeyValuePair<string, string>("fileName", DeviceThemeFilePath),
                    });
                    break;

                case CommandId.SetDeviceReload:
                    replies.Add(() => Reply(CommandId.SetDeviceReload, body));
                    break;
            }
        }

        private void Reply(CommandId command, byte[] payload)
        {
            var frame = new DeviceFrame((uint)PacketType.AckReply, (uint)command, payload, 0, true);
            DataReceived?.Invoke(this, frame.Encode());
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        public void RaiseError(Exception ex) => ErrorOccurred?.Invoke(this, ex);
    }

    [Test]
    public async Task UploadThemeFile_MatchesTheVendorMessageSequence()
    {
        byte[] themeBytes = FiveKeyTestThemeTests.BuildFiveKeyTestTheme();

        var transport = new FakeDeviceTransport();
        await using var client = new Mk20DeviceClient(transport);
        await client.ConnectAsync();

        await client.UploadThemeAsync(ThemeName, themeBytes, TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(transport.Sequence, Is.EqualTo(new[]
            {
                nameof(CommandId.GetDeviceTheme),
                "ABORT",
                nameof(CommandId.FileStart),
                nameof(CommandId.FileEnd),
                "ABORT",
                nameof(CommandId.SetDeviceReload),
            }), "host-side message order must match the vendor's, and the abort must close the " +
                "bulk stream before the device can see FILE_END");

            Assert.That(transport.RawFileBytes, Is.EqualTo(themeBytes.Length),
                "the bulk payload must be exactly the file's bytes, with no extra framing");
        });
    }
}
