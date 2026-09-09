"""List a storey's wall panels (extents, length, thickness) from an e2k; optionally only those near a corner.
import os
usage: storey_walls.py <e2k> <storey> [minLen]"""
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from e2k_read import read
path, storey = sys.argv[1], sys.argv[2]
min_len = float(sys.argv[3]) if len(sys.argv) > 3 else 0
order, area, line, kind, on = read(path)
objs = on.get(storey, [])
rows = []
for o in objs:
    if kind.get(o) != "PANEL" or o not in area: continue
    pts = area[o]
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    length, thick = (w, h) if w >= h else (h, w)
    if length < min_len: continue
    rows.append((length, o, min(xs), max(xs), min(ys), max(ys), thick))
rows.sort(reverse=True)
print(f"{storey}: {len(rows)} panels {min_len:.0f}+ long")
for length, o, x0, x1, y0, y1, thick in rows:
    print(f"  {o:8s} len {length:8.0f}  x {x0:9.0f}..{x1:9.0f}  y {y0:9.0f}..{y1:9.0f}  bbox-thick {thick:6.0f}")
