"""Segment lengths around one storey's plate: a staircase is many short alternating runs."""
import collections
import re
import sys

PT = re.compile(r'^\s*POINT\s+"([^"]+)"\s+(-?[\d.]+)\s+(-?[\d.]+)')
AREA = re.compile(r'^\s*AREA\s+"(KF\d+)"\s+FLOOR\s+(\d+)\s+(.*)$')
ASSIGN = re.compile(r'^\s*AREAASSIGN\s+"(KF\d+)"\s+"([^"]+)"')

path, want = sys.argv[1], sys.argv[2]

pts, ring = {}, {}
for line in open(path, encoding="utf-8", errors="replace"):
    m = PT.match(line)
    if m:
        pts[m.group(1)] = (float(m.group(2)), float(m.group(3)))
        continue
    m = AREA.match(line)
    if m:
        names = re.findall(r'"([^"]+)"', m.group(3))[: int(m.group(2))]
        ring[m.group(1)] = [pts[n] for n in names if n in pts]

for line in open(path, encoding="utf-8", errors="replace"):
    m = ASSIGN.match(line)
    if not m or m.group(2).upper() != want.upper() or m.group(1) not in ring:
        continue

    r = ring[m.group(1)]
    lens = []
    for a, b in zip(r, r[1:] + r[:1]):
        dx, dy = b[0] - a[0], b[1] - a[1]
        lens.append((abs(dx) + abs(dy), "H" if abs(dy) < 0.5 else ("V" if abs(dx) < 0.5 else "D")))

    buckets = collections.Counter()
    for length, kind in lens:
        if length < 12:
            buckets["under 1 ft"] += 1
        elif length < 36:
            buckets["1-3 ft"] += 1
        elif length < 120:
            buckets["3-10 ft"] += 1
        else:
            buckets["over 10 ft"] += 1

    kinds = collections.Counter(k for _, k in lens)
    print(f"{m.group(1)} on {want}: {len(r)} points")
    print("  segment lengths:", dict(buckets))
    print("  directions     :", dict(kinds), " (H horizontal, V vertical, D diagonal)")

    short = [(round(l, 1), k) for l, k in lens if l < 36]
    print(f"  {len(short)} segment(s) under 3 ft, first 24: {short[:24]}")
