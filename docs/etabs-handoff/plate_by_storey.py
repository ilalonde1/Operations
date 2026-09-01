"""Floor plate area per storey, plus each plate's bounding box, from a shipped e2k."""
import collections
import re
import sys

PT = re.compile(r'^\s*POINT\s+"([^"]+)"\s+(-?[\d.]+)\s+(-?[\d.]+)')
AREA = re.compile(r'^\s*AREA\s+"(KF\d+)"\s+FLOOR\s+(\d+)\s+(.*)$')
ASSIGN = re.compile(r'^\s*AREAASSIGN\s+"(KF\d+)"\s+"([^"]+)"')

path = sys.argv[1]
want = sys.argv[2] if len(sys.argv) > 2 else None

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

by_storey = collections.defaultdict(list)
for line in open(path, encoding="utf-8", errors="replace"):
    m = ASSIGN.match(line)
    if m and m.group(1) in ring:
        by_storey[m.group(2)].append(m.group(1))

for storey in sorted(by_storey):
    if want and want.upper() not in storey.upper():
        continue
    for name in by_storey[storey]:
        r = ring[name]
        if len(r) < 3:
            continue
        s = sum(r[i][0] * r[(i + 1) % len(r)][1] - r[(i + 1) % len(r)][0] * r[i][1]
                for i in range(len(r)))
        area = abs(s) / 2.0 / 144.0
        xs = [p[0] for p in r]
        ys = [p[1] for p in r]
        print(f"{storey:14s} {name:6s} {area:10,.0f} sq ft   {len(r):3d} pts   "
              f"bbox {max(xs) - min(xs):7.0f} x {max(ys) - min(ys):7.0f} in   "
              f"at ({min(xs):.0f},{min(ys):.0f})")
        if want:
            for p in r:
                print(f"      ({p[0]:9.1f}, {p[1]:9.1f})")
