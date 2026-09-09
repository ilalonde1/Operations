"""Draw one storey of an e2k large: walls dark red (thickness shown), columns green, plates blue.
usage: draw_storey.py <e2k> <storey> <out.svg> [size px]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from e2k_read import read
path, storey, out = sys.argv[1], sys.argv[2], sys.argv[3]
size = int(sys.argv[4]) if len(sys.argv) > 4 else 1600
order, area, line, kind, on = read(path)
# objects on this storey: read() returns per-object geometry; 'on' maps storey -> set of objects? adapt to its shape
objs = on.get(storey)
if objs is None:
    print("storeys:", ", ".join(order)); sys.exit(1)
pts = []
for o in objs:
    if o in area: pts += area[o]
    if o in line: pts += line[o]
if not pts:
    print("nothing on", storey); sys.exit(1)
minx = min(p[0] for p in pts); maxx = max(p[0] for p in pts)
miny = min(p[1] for p in pts); maxy = max(p[1] for p in pts)
w, h = maxx - minx, maxy - miny
sc = size / max(w, h)
def xy(p): return ((p[0] - minx) * sc + 20, (maxy - p[1]) * sc + 20)
parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{w*sc+40:.0f}" height="{h*sc+40:.0f}" style="background:#fff">']
for o in objs:
    k = kind.get(o, "")
    if o in area:
        d = " ".join(f"{x:.1f},{y:.1f}" for x, y in map(xy, area[o]))
        if k == "FLOOR": parts.append(f'<polygon points="{d}" fill="#4a90d9" fill-opacity="0.25" stroke="#1f5fa8" stroke-width="1"/>')
        else: parts.append(f'<polygon points="{d}" fill="none" stroke="#8b0000" stroke-width="1.5"/>')
    elif o in line:
        p = line[o]
        if len(p) >= 2 and k in ("BEAM", "BRACE"):
            a, b = xy(p[0]), xy(p[1]); parts.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#7d3c98" stroke-width="1"/>')
        else:
            x, y = xy(p[0]); parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="2.5" fill="#1e8449"/>')
parts.append(f'<text x="24" y="{h*sc+36:.0f}" font-family="monospace" font-size="14">{storey}: {len(objs)} objects, extents {w:.0f} x {h:.0f} units</text>')
parts.append("</svg>")
open(out, "w", encoding="utf-8").write("\n".join(parts))
print(out, len(objs), "objects")
