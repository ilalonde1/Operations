r"""zonelook.py <dxf> <png> : draw a built DXF's floor rings (KOR_C_SLABEDG) and thickness zones (KOR_C_SLABZONE), each zone
filled by the «N SLAB» text inside it and labelled with its area, to LOOK at step 183's output. COVERS: closed polylines on
those two layers, TEXT entities naming SLAB. DOES NOT: block inserts, what the classifier then makes of the zones."""
import re, sys, collections
import ezdxf
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon as MPoly
from matplotlib.path import Path

doc = ezdxf.readfile(sys.argv[1]); msp = doc.modelspace()
def pts(e): return [(p[0], p[1]) for p in (e.get_points() if e.dxftype() == "LWPOLYLINE" else e.points())]
floors = [pts(e) for e in msp.query("LWPOLYLINE POLYLINE") if e.dxf.layer == "KOR_C_SLABEDG"]
zones = [pts(e) for e in msp.query("LWPOLYLINE POLYLINE") if e.dxf.layer == "KOR_C_SLABZONE"]
tags = []
for e in msp.query("TEXT MTEXT"):
    t = e.plain_text() if e.dxftype() == "MTEXT" else e.dxf.text
    m = re.search(r"(\d+(?:\.\d+)?)\s*\"?\s*SLAB", t.upper())
    if m: tags.append((e.dxf.insert.x, e.dxf.insert.y, float(m.group(1))))
vals = sorted({v for _, _, v in tags}); cm = plt.get_cmap("tab10"); col = {v: cm(i % 10) for i, v in enumerate(vals)}
def area(p): return abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p)))) / 2
fig, ax = plt.subplots(figsize=(18, 13))
for z in zones:
    path = Path(z); inside = collections.Counter(v for x, y, v in tags if path.contains_point((x, y)))
    v = inside.most_common(1)[0][0] if inside else None
    ax.add_patch(MPoly(z, closed=True, fc=(*col[v][:3], 0.4) if v else (0, 0, 0, 0.05), ec="k", lw=1.2))
    cx = sum(p[0] for p in z) / len(z); cy = sum(p[1] for p in z) / len(z)
    ax.text(cx, cy, f"{v:g}\n{area(z) / 92903.04:,.0f} sq ft" if v else f"? {area(z) / 92903.04:,.0f}", fontsize=8, ha="center")
for f in floors: ax.plot(*zip(*(f + f[:1])), "r-", lw=0.6)
for x, y, v in tags: ax.plot(x, y, "o", color=col[v], ms=4)
ax.autoscale(); ax.set_aspect("equal"); plt.savefig(sys.argv[2], dpi=80, bbox_inches="tight")
print(len(floors), "floor ring(s),", len(zones), "zone(s),", len(tags), "call-out(s)")
