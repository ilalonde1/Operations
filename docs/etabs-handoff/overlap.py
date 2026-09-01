"""Do columns and walls overlap: same object on several storeys, or two objects on one spot."""
import collections
import re
import sys

LINE = re.compile(r'^\s*LINE\s+"(K[CW]\d+)"\s+(\w+)\s+"([^"]+)"\s+"([^"]+)"')
AREA = re.compile(r'^\s*AREA\s+"(KW\d+)"\s+(\d+)\s+(.*)$')
LASSIGN = re.compile(r'^\s*LINEASSIGN\s+"(K\w+\d+)"\s+"([^"]+)"')
AASSIGN = re.compile(r'^\s*AREAASSIGN\s+"(K\w+\d+)"\s+"([^"]+)"')
PT = re.compile(r'^\s*POINT\s+"([^"]+)"\s+(-?[\d.]+)\s+(-?[\d.]+)')

path = sys.argv[1]
pts, conn, storeys = {}, {}, collections.defaultdict(list)

for line in open(path, encoding="utf-8", errors="replace"):
    m = PT.match(line)
    if m:
        pts[m.group(1)] = (float(m.group(2)), float(m.group(3)))
        continue
    m = LINE.match(line)
    if m:
        conn[m.group(1)] = (m.group(2), m.group(3), m.group(4))
        continue
    for rx in (LASSIGN, AASSIGN):
        m = rx.match(line)
        if m:
            storeys[m.group(1)].append(m.group(2))
            break

multi = {o: s for o, s in storeys.items() if len(s) > 1 and o.startswith(("KC", "KW"))}
print(f"objects assigned to more than one storey: {len(multi)}")
for o, s in list(multi.items())[:8]:
    print(f"   {o}: {', '.join(s)}")

# Two columns on one storey at the same plan position = an overlap in ETABS.
spot = collections.defaultdict(list)
for obj, (kind, a, b) in conn.items():
    if not obj.startswith("KC") or a not in pts:
        continue
    for st in storeys.get(obj, []):
        spot[(st, round(pts[a][0], 1), round(pts[a][1], 1))].append(obj)

dup = {k: v for k, v in spot.items() if len(v) > 1}
print(f"columns sharing one storey and one plan position: {len(dup)}")
for (st, x, y), objs in list(dup.items())[:8]:
    print(f"   {st} at ({x},{y}): {', '.join(objs)}")
