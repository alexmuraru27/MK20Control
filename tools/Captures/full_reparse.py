"""
Complete from-scratch re-parse of a pcapng capture. No assumptions, no reliance on any
prior script's output. Extracts EVERY USB frame for the MK20 device address (both
directions), reconstructs the full continuous byte stream for each direction in exact
capture order, and walks the ENTIRE stream byte-by-byte re-detecting every command frame,
abort-message, and byte range in between - printing everything found, with no filtering
based on expectations.
"""
import subprocess, sys, struct, os

TSHARK = r"C:\Program Files\Wireshark\tshark.exe"

HEADER = b"AA551234 FIXEDCMDHEAD "
ABORT = b"AA551234 Abort file transfer 123455AA"

CMD_NAMES = {
    0: "FindDevice", 1: "SendSystemDataToDevice", 2: "SetDeviceReload", 3: "GetDeviceTheme",
    4: "SetDeviceBacklight", 5: "cmd5", 6: "FileStart", 7: "FileEnd", 8: "cmd8", 9: "cmd9",
    10: "cmd10", 11: "SetDeviceDeleteTheme", 12: "SendPixmap", 13: "DeviceProactiveEscalationCmd",
    14: "cmd14", 15: "SendJson",
}


def get_device_addresses(path):
    """Detect the MK20 device's USB address by finding which address's OUT or IN payload
    field actually contains the fixed ASCII command header bytes - checked directly against
    both possible payload fields tshark could expose for this capture (usbcom.data.out_payload/
    in_payload), not assumed to be one specific field name."""
    header_colon_hex = ":".join(f"{b:02x}" for b in HEADER)
    addrs = set()
    for field in ("usbcom.data.out_payload", "usbcom.data.in_payload"):
        out = subprocess.run([TSHARK, "-r", path, "-Y", f'{field} contains "{HEADER.decode()}"',
                               "-T", "fields", "-e", "usb.device_address"],
                              capture_output=True, text=True)
        for x in out.stdout.split():
            if x:
                addrs.add(int(x))
    return sorted(addrs)


def get_all_frames_for_address(path, addr):
    """Pull every single frame (both directions) for this address, raw, no filtering by
    content, ordered by frame number (tshark's natural capture order)."""
    out = subprocess.run([TSHARK, "-r", path, "-Y", f"usb.device_address=={addr}",
                           "-T", "fields", "-e", "frame.number", "-e", "frame.time_relative",
                           "-e", "usbcom.data.out_payload", "-e", "usbcom.data.in_payload"],
                          capture_output=True, text=True)
    rows = []
    for line in out.stdout.splitlines():
        parts = line.split("\t")
        if len(parts) < 2:
            continue
        fn = int(parts[0])
        t = float(parts[1])
        out_hex = parts[2] if len(parts) > 2 else ""
        in_hex = parts[3] if len(parts) > 3 else ""
        if out_hex:
            rows.append((fn, t, "OUT", bytes.fromhex(out_hex)))
        if in_hex:
            rows.append((fn, t, "IN", bytes.fromhex(in_hex)))
    return rows


def reconstruct_and_parse(rows, direction, label):
    """Concatenate ALL bytes for this direction into one continuous buffer (tracking the
    originating frame/time of every byte offset), then walk the ENTIRE buffer from position
    0 re-detecting every recognizable structure: command frames, abort messages, and
    unrecognized byte ranges (printed as raw byte counts, not silently skipped)."""
    buf = bytearray()
    frame_bounds = []  # (start_offset, end_offset, frame_number, time)
    for fn, t, d, data in rows:
        if d != direction:
            continue
        start = len(buf)
        buf.extend(data)
        frame_bounds.append((start, len(buf), fn, t))
    buf = bytes(buf)
    total_len = len(buf)
    print(f"\n--- {label}: {len(frame_bounds)} raw USB frames, {total_len} total bytes reconstructed ---")

    def locate(offset):
        # binary-search-free linear scan is fine here for correctness verification
        best = frame_bounds[0] if frame_bounds else (0, 0, 0, 0.0)
        for fb in frame_bounds:
            if fb[0] <= offset < fb[1]:
                return fb
            if fb[0] > offset:
                break
            best = fb
        return best

    pos = 0
    unrecognized_start = None
    results = []
    while pos < total_len:
        if buf[pos:pos + len(ABORT)] == ABORT:
            if unrecognized_start is not None:
                results.append(("RAW", unrecognized_start, pos, pos - unrecognized_start))
                unrecognized_start = None
            fb = locate(pos)
            results.append(("ABORT", pos, pos + len(ABORT), fb))
            pos += len(ABORT)
            continue
        if buf[pos:pos + len(HEADER)] == HEADER:
            hdr_end = pos + len(HEADER)
            if hdr_end + 16 > total_len:
                # incomplete header tail - record as raw/truncated, stop trying to parse further as a command
                if unrecognized_start is None:
                    unrecognized_start = pos
                pos += 1
                continue
            ptype, cmd, plen = struct.unpack_from("<III", buf, hdr_end)
            total = 16 + plen
            if hdr_end + total > total_len:
                # payload not fully present (could be genuinely incomplete in this capture window)
                if unrecognized_start is None:
                    unrecognized_start = pos
                pos += 1
                continue
            if unrecognized_start is not None:
                results.append(("RAW", unrecognized_start, pos, pos - unrecognized_start))
                unrecognized_start = None
            payload = buf[hdr_end + 16: hdr_end + 16 + plen]
            fb = locate(pos)
            fb_end = locate(pos + len(HEADER) + total - 1)
            results.append(("CMD", pos, pos + len(HEADER) + total, (cmd, plen, payload, fb, fb_end)))
            pos = hdr_end + total
            continue
        if unrecognized_start is None:
            unrecognized_start = pos
        pos += 1
    if unrecognized_start is not None:
        results.append(("RAW", unrecognized_start, total_len, total_len - unrecognized_start))

    for r in results:
        kind = r[0]
        if kind == "ABORT":
            _, s, e, fb = r
            print(f"  [{s:>10}-{e:<10}] ABORT-FILE-TRANSFER  (frame={fb[2]} t={fb[3]:.6f})")
        elif kind == "CMD":
            _, s, e, (cmd, plen, payload, fb_start, fb_end) = r
            name = CMD_NAMES.get(cmd, f"cmd{cmd}")
            extra = ""
            if cmd == 2 and plen > 0:
                try:
                    extra = " path=" + payload.decode("utf-8", errors="replace")
                except Exception:
                    pass
            elif cmd in (6, 7) and plen > 0 and plen < 300:
                try:
                    extra = " payload=" + payload.decode("utf-16-le", errors="replace")
                except Exception:
                    pass
            print(f"  [{s:>10}-{e:<10}] CMD cmd={cmd}({name}) payloadLen={plen} "
                  f"(startFrame={fb_start[2]} t={fb_start[3]:.6f} -> endFrame={fb_end[2]} t={fb_end[3]:.6f}){extra}")
        else:  # RAW
            _, s, e, n = r
            fb = locate(s)
            print(f"  [{s:>10}-{e:<10}] RAW/bulk-data {n} byte(s)  (starts at frame={fb[2]} t={fb[3]:.6f})")


def main():
    path = sys.argv[1]
    addrs = get_device_addresses(path)
    print(f"=== FULL FROM-SCRATCH RE-PARSE: {path} ===")
    print(f"Device address(es) found: {addrs}")
    for addr in addrs:
        rows = get_all_frames_for_address(path, addr)
        print(f"\n=== Address {addr}: {len(rows)} total raw frame-rows pulled from tshark ===")
        reconstruct_and_parse(rows, "OUT", f"address {addr} OUT stream")
        reconstruct_and_parse(rows, "IN", f"address {addr} IN stream")


if __name__ == "__main__":
    main()
