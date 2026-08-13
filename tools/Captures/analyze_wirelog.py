import sys, statistics

def analyze(path):
    rows = []
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 3:
                continue
            t = float(parts[0])
            direction = parts[1]
            data = bytes.fromhex(parts[2])
            rows.append((t, direction, data))

    print(f"=== {path} ===")
    print(f"Total lines: {len(rows)}")
    out_rows = [(t, d) for t, direction, d in rows if direction == "OUT"]
    in_rows = [(t, d) for t, direction, d in rows if direction == "IN"]
    print(f"OUT frames: {len(out_rows)}, IN frames: {len(in_rows)}")
    if out_rows:
        print(f"First OUT t={out_rows[0][0]:.6f}  Last OUT t={out_rows[-1][0]:.6f}  total elapsed={out_rows[-1][0]-out_rows[0][0]:.6f}s")
        sizes = [len(d) for _, d in out_rows]
        print(f"OUT sizes: min={min(sizes)} max={max(sizes)} total={sum(sizes)}")
        deltas = [ (out_rows[i][0]-out_rows[i-1][0])*1000 for i in range(1, len(out_rows)) ]
        if deltas:
            print(f"Inter-OUT-write gap (ms): min={min(deltas):.4f} max={max(deltas):.4f} mean={statistics.mean(deltas):.4f} median={statistics.median(deltas):.4f}")
            big = [(i, g) for i, g in enumerate(deltas) if g > 5]
            print(f"Gaps > 5ms: {len(big)}")
            for i, g in big[:20]:
                print(f"  idx={i} gap={g:.3f}ms  size_before={sizes[i]} size_after={sizes[i+1]}")
    if in_rows:
        print(f"\nLast IN frame at t={in_rows[-1][0]:.6f}")
    print()

for p in sys.argv[1:]:
    analyze(p)
