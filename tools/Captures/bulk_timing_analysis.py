import subprocess, sys, struct, os, statistics
sys.stdout.reconfigure(encoding="utf-8")

TSHARK = r"C:\Program Files\Wireshark\tshark.exe"
CAPDIR = r"C:\Users\Alex\Desktop\MK20Control\tools\Captures"

CMD_NAMES = {
    0: "FindDevice", 1: "SendSystemDataToDevice", 2: "SetDeviceReload", 3: "GetDeviceTheme",
    4: "SetDeviceBacklight", 6: "FileStart", 7: "FileEnd", 11: "SetDeviceDeleteTheme",
    12: "SendPixmap", 13: "DeviceProactiveEscalationCmd", 15: "SendJson",
}
HEADER = b"AA551234 FIXEDCMDHEAD "
ABORT = b"AA551234 Abort file transfer 123455AA"


def get_device_address(path):
    out = subprocess.run([TSHARK, "-r", path, "-Y", "usb.idVendor==0x1d6b || usb.idVendor==0x1234",
                           "-T", "fields", "-e", "usb.device_address"], capture_output=True, text=True)
    addrs = sorted(set(int(x) for x in out.stdout.split() if x))
    return addrs


def extract_out_frames(path, addr):
    """Every OUT (host->device) USB frame for this address, in exact chronological/frame.number
    order, with its exact per-frame byte length and timestamp - the ground truth for real
    inter-write timing, independent of any higher-level command decoding."""
    out = subprocess.run([TSHARK, "-r", path, "-Y", f"usb.device_address=={addr}",
                           "-T", "fields", "-e", "frame.number", "-e", "frame.time_relative",
                           "-e", "usbcom.data.out_payload"],
                          capture_output=True, text=True)
    rows = []
    for line in out.stdout.splitlines():
        parts = line.split("\t")
        if len(parts) < 3 or not parts[2]:
            continue
        fn = int(parts[0]); t = float(parts[1])
        data = bytes.fromhex(parts[2])
        rows.append((fn, t, data))
    return rows


def find_command_offsets(rows):
    """Reconstruct the continuous OUT byte stream (tracking each byte's originating frame +
    timestamp), then locate every FILE_START/FILE_END command frame's exact byte offset so we
    can slice out the raw bulk-data byte range between them precisely."""
    buf = bytearray()
    frame_of_offset = []  # (offset, frame_number, time) - one entry per contributing frame
    for fn, t, data in rows:
        frame_of_offset.append((len(buf), fn, t))
        buf.extend(data)
    buf = bytes(buf)

    commands = []  # (byte_offset, frame_number, time, cmd_id, payload_len)
    pos = 0
    while pos < len(buf):
        if buf[pos:pos+len(ABORT)] == ABORT:
            pos += len(ABORT)
            continue
        if buf[pos:pos+len(HEADER)] == HEADER:
            hdr_end = pos + len(HEADER)
            if hdr_end + 16 > len(buf):
                break
            ptype, cmd, plen = struct.unpack_from("<III", buf, hdr_end)
            total = 16 + plen
            if hdr_end + total > len(buf):
                break
            # locate frame/time for this offset
            best = frame_of_offset[0]
            for fo in frame_of_offset:
                if fo[0] <= pos: best = fo
                else: break
            commands.append((pos, best[1], best[2], cmd, plen))
            pos = hdr_end + total
            continue
        pos += 1
    return buf, frame_of_offset, commands


def analyze_bulk_transfer(rows, buf, frame_of_offset, start_offset, end_offset):
    """Given the exact byte range [start_offset, end_offset) that is pure bulk file data
    (between a FileStart command's end and the next FileEnd command's start), find every
    contributing raw OUT frame and compute exact per-frame-write timing."""
    contributing = [fo for fo in frame_of_offset if start_offset <= fo[0] < end_offset]
    if not contributing:
        return None
    times = [c[2] for c in contributing]
    # frame sizes: need to know how many bytes each contributed within this range specifically
    sizes = []
    for i, fo in enumerate(contributing):
        this_start = fo[0]
        next_start = contributing[i+1][0] if i+1 < len(contributing) else end_offset
        sizes.append(next_start - this_start)
    total_bytes = sum(sizes)
    elapsed = times[-1] - times[0] if len(times) > 1 else 0.0
    deltas_ms = [ (times[i]-times[i-1])*1000 for i in range(1, len(times)) ]
    return {
        "num_frames": len(contributing),
        "total_bytes": total_bytes,
        "elapsed_s": elapsed,
        "throughput_MBps": (total_bytes/elapsed/1_000_000) if elapsed > 0 else float("inf"),
        "deltas_ms": deltas_ms,
        "first_frame": contributing[0][1],
        "last_frame": contributing[-1][1],
        "first_t": times[0],
        "last_t": times[-1],
    }


def analyze_capture(fname):
    path = os.path.join(CAPDIR, fname)
    print(f"\n{'='*100}\n=== {fname} ===")
    addrs = get_device_address(path)
    if not addrs:
        print("  No MK20 device address found.")
        return
    for addr in addrs:
        rows = extract_out_frames(path, addr)
        if not rows:
            continue
        buf, frame_of_offset, commands = find_command_offsets(rows)
        file_starts = [c for c in commands if c[3] == 6]
        file_ends = [c for c in commands if c[3] == 7]
        print(f"  address {addr}: {len(rows)} raw OUT frames, {len(commands)} command frames "
              f"({len(file_starts)} FileStart, {len(file_ends)} FileEnd)")

        for fs in file_starts:
            fs_offset, fs_fn, fs_t, fs_cmd, fs_plen = fs
            bulk_start = fs_offset + len(HEADER) + 16 + fs_plen
            later_ends = [fe for fe in file_ends if fe[0] > bulk_start]
            if not later_ends:
                continue
            fe = min(later_ends, key=lambda x: x[0])
            fe_offset, fe_fn, fe_t, fe_cmd, fe_plen = fe

            result = analyze_bulk_transfer(rows, buf, frame_of_offset, bulk_start, fe_offset)
            print(f"\n  --- Bulk region: FileStart frame={fs_fn} t={fs_t:.4f}  ->  FileEnd frame={fe_fn} t={fe_t:.4f} ---")
            if result is None:
                print("    (no bulk bytes between these - re-activation without transfer)")
                continue
            print(f"    Raw OUT frames carrying bulk data: {result['num_frames']}, total bytes: {result['total_bytes']}")
            print(f"    First bulk frame={result['first_frame']} t={result['first_t']:.6f}  Last bulk frame={result['last_frame']} t={result['last_t']:.6f}")
            print(f"    Elapsed host-side write time: {result['elapsed_s']*1000:.3f} ms  =>  throughput {result['throughput_MBps']:.3f} MB/s")
            d = result["deltas_ms"]
            if d:
                print(f"    Inter-frame-write gap (ms): min={min(d):.4f} max={max(d):.4f} mean={statistics.mean(d):.4f} median={statistics.median(d):.4f}")
                # Flag any unusually large gaps (potential stalls) within the bulk transfer
                big = [(i, g) for i, g in enumerate(d) if g > 20]
                if big:
                    print(f"    ** {len(big)} gap(s) > 20ms found within the bulk transfer (potential stall points): "
                          + ", ".join(f"idx={i} gap={g:.2f}ms" for i, g in big[:10]))
            gap_to_fileend = (fe_t - result['last_t']) * 1000
            print(f"    Gap from last bulk write to FileEnd command being sent: {gap_to_fileend:.3f} ms")


if __name__ == "__main__":
    for fname in sys.argv[1:]:
        try:
            analyze_capture(fname)
        except Exception as e:
            import traceback
            print(f"  ERROR analyzing {fname}: {e}")
            traceback.print_exc()
