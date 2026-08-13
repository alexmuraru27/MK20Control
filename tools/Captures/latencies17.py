import re, sys

pattern = re.compile(r"t=\s*([\d.]+) frame=\s*(\d+) \[(OUT|IN)\] type=\? cmd=(\d+)\((\w+)\)(?: len=(\d+))?(?: path=(.*))?")

def parse(path):
    events = []
    for line in open(path, encoding="utf-8"):
        m = pattern.search(line)
        if m:
            t, fn, d, cmd, name, ln, p = m.groups()
            events.append({"t": float(t), "fn": int(fn), "dir": d, "cmd": int(cmd), "name": name, "path": p})
    return events

events = parse(sys.argv[1])

# Walk through and find FileEnd OUT->IN and SetDeviceReload OUT->IN latencies, in order
i = 0
print(f"{'cmd':12} {'path':50} {'sent_t':>10} {'acked_t':>10} {'latency_ms':>12}")
pending = {}
for e in events:
    key = e["name"]
    if e["dir"] == "OUT" and key in ("FileEnd", "SetDeviceReload", "FileStart"):
        pending.setdefault(key, []).append(e)
    elif e["dir"] == "IN" and key in ("FileEnd", "SetDeviceReload", "FileStart"):
        if pending.get(key):
            sent = pending[key].pop(0)
            latency = (e["t"] - sent["t"]) * 1000
            path = e["path"] or sent["path"] or ""
            print(f"{key:12} {path:50} {sent['t']:10.4f} {e['t']:10.4f} {latency:12.2f}")
