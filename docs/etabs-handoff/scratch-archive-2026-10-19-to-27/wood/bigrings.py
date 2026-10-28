r"""bigrings.py <dxf> [min sq ft] : closed polylines on slab layers of one DXF, largest first, with their box, against the box of
the KOR_C_WALL layer - to find where a floor plate the composer took lies relative to the plan's walls."""
import sys
import ezdxf
from shapely.geometry import Polygon

SQ = 92903.04
doc = ezdxf.readfile(sys.argv[1]); msp = doc.modelspace()
lo = float(sys.argv[2]) if len(sys.argv) > 2 else 500


def pts(e): return [(p[0], p[1]) for p in (e.get_points() if e.dxftype() == "LWPOLYLINE" else e.points())]


xs, ys = [], []
for e in msp.query("LWPOLYLINE POLYLINE LINE"):
    if e.dxf.layer != "KOR_C_WALL": continue
    p = [(e.dxf.start.x, e.dxf.start.y), (e.dxf.end.x, e.dxf.end.y)] if e.dxftype() == "LINE" else pts(e)
    xs += [q[0] for q in p]; ys += [q[1] for q in p]
print("walls box (mm):", (round(min(xs)), round(min(ys)), round(max(xs)), round(max(ys))) if xs else "no walls")
rings = []
for e in msp.query("LWPOLYLINE POLYLINE"):
    if not e.dxf.layer.startswith("KOR_C_SLAB"): continue
    p = pts(e)
    if len(p) < 3: continue
    a = Polygon(p).buffer(0).area / SQ
    if a >= lo: rings.append((a, e.dxf.layer, Polygon(p).bounds, len(p)))
for a, layer, b, n in sorted(rings, reverse=True):
    print(f"{a:9,.0f} sq ft  {layer:<16} {n:4} vertices  box {tuple(round(v) for v in b)}")
print(len(rings), "rings of", lo, "sq ft or more")
