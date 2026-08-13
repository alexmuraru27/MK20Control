import subprocess, sys, struct, os
sys.stdout.reconfigure(encoding="utf-8")

TSHARK = r"C:\Program Files\Wireshark\tshark.exe"
CAPDIR = r"C:\Users\Alex\Desktop\MK20Control\tools\Captures"

CMD_NAMES = {
    0: "FindDevice", 1: "SendSystemDataToDevice", 2: "SetDeviceReload", 3: "GetDeviceTheme",
    4: "SetDeviceBacklight", 5: "cmd5", 6: "FileStart", 7: "FileEnd", 8: "cmd8", 9: "cmd9",
    10: "cmd10", 11: "SetDeviceDeleteTheme", 12: "SendPixmap", 13: "DeviceProactiveEscalationCmd",
    14: "cmd14", 15: "SendJson",
}
HEADER = b"AA551234 FIXEDCMDHEAD "
ABORT = b"AA551234 Abort file transfer 123455AA"

def get_device_address(path):
    out = subprocess.run([TSHARK, "-r", path, "-Y", "usb.idVendor==0x1d6b || usb.idVendor==0x1234",
                           "-T", "fields", "-e", "usb.device_address"], capture_output=True, text=True)
    addrs = sorted(set(int(x) for x in out.stdout.split() if x))
    return addrs

def extract_frames_chronological(path, addr):
    """Query tshark ONCE for both directions together, in true ascending frame.number order
    (tshark's natural output order = capture chronological order). Each row has at most one
    of the two payload fields populated (out or in) depending on direction."""
    out = subprocess.run([TSHARK, "-r", path, "-Y", f"usb.device_address=={addr}",
                           "-T", "fields", "-e", "frame.number", "-e", "frame.time_relative",
                           "-e", "usbcom.data.out_payload", "-e", "usbcom.data.in_payload"],
                          capture_output=True, text=True)
    rows = []
    for line in out.stdout.splitlines():
        parts = line.split("\t")
        if len(parts) < 2: continue
        fn = int(parts[0]); t = float(parts[1])
        out_hex = parts[2] if len(parts) > 2 else ""
        in_hex = parts[3] if len(parts) > 3 else ""
        if out_hex:
            rows.append((fn, t, "OUT", bytes.fromhex(out_hex)))
        if in_hex:
            rows.append((fn, t, "IN", bytes.fromhex(in_hex)))
    return rows  # already in ascending frame.number order from tshark

def decode_stream(rows, direction):
    """Concatenate all bytes for one direction (preserving original frame numbers/times per
    byte-offset) and parse frames using the confirmed header format, tracking exactly which
    original frame(s) contributed to each parsed frame."""
    buf = bytearray()
    offsets = []  # (byte_offset_start, frame_number, time)
    for fn, t, d, data in rows:
        if d != direction: continue
        offsets.append((len(buf), fn, t))
        buf.extend(data)
    buf = bytes(buf)

    def locate(pos):
        best = offsets[0] if offsets else (0, 0, 0.0)
        for o in offsets:
            if o[0] <= pos: best = o
            else: break
        return best  # (offset, frame_number, time)

    events = []
    pos = 0
    while pos < len(buf):
        if buf[pos:pos+len(ABORT)] == ABORT:
            _, fn, t = locate(pos)
            events.append({"pos": pos, "fn": fn, "t": t, "dir": direction, "kind": "ABORT"})
            pos += len(ABORT)
            continue
        if buf[pos:pos+len(HEADER)] == HEADER:
            hdr_end = pos + len(HEADER)
            if hdr_end + 16 > len(buf):
                break  # incomplete tail, stop (would need more data)
            ptype, cmd, plen = struct.unpack_from("<III", buf, hdr_end)
            total = 16 + plen
            if hdr_end + total > len(buf):
                break  # payload not fully captured yet
            payload = buf[hdr_end+16: hdr_end+16+plen]
            _, fn, t = locate(pos)
            events.append({"pos": pos, "fn": fn, "t": t, "dir": direction, "kind": "CMD",
                           "cmd": cmd, "len": plen, "payload": payload})
            pos = hdr_end + total
            continue
        pos += 1  # resync
    return events

def main():
    fname = sys.argv[1]
    path = os.path.join(CAPDIR, fname)
    addrs = get_device_address(path)
    print(f"=== {fname} === MK20-matching device addresses found: {addrs}")
    for addr in addrs:
        rows = extract_frames_chronological(path, addr)
        out_events = decode_stream(rows, "OUT")
        in_events = decode_stream(rows, "IN")
        all_events = sorted(out_events + in_events, key=lambda e: (e["t"], e["fn"]))
        print(f"--- address {addr}: {len(all_events)} total events (raw byte-level parse, no assumptions) ---")
        for e in all_events:
            if e["kind"] == "ABORT":
                print(f"  t={e['t']:10.4f} frame={e['fn']:6d} [{e['dir']}] ABORT-FILE-TRANSFER")
            else:
                name = CMD_NAMES.get(e["cmd"], f"cmd{e['cmd']}")
                extra = ""
                if e["cmd"] == 2 and e["len"] > 0:
                    try: extra = " path=" + e["payload"].decode("utf-8", errors="replace")
                    except Exception: pass
                print(f"  t={e['t']:10.4f} frame={e['fn']:6d} [{e['dir']}] type=? cmd={e['cmd']}({name}) len={e['len']}{extra}")

if __name__ == "__main__":
    main()
