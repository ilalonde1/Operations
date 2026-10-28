r"""zoneexcess.py <dxf> : for each floor ring (KOR_C_SLABEDG) holding KOR_C_SLABZONE zones, how much zone area lies OUTSIDE
the floor and how much zones overlap each other, with the worst pieces' places. COVERS: closed polylines on those two
layers of one DXF. DOES NOT: the classifier's tiling test (2%), what the composer then writes."""
import sys
import ezdxf
from shapely.geometry import Polygon
from shapely.ops import unary_union

doc = ezdxf.readfile(sys.argv[1]); msp = doc.modelspace()
def pts(e): return [(p[0], p[1]) for p in (e.get_points() if e.dxftype() == "LWPOLYLINE" else e.points())]
SQ = 92903.04
floors = [Polygon(pts(e)).buffer(0) for e in msp.query("LWPOLYLINE POLYLINE") if e.dxf.layer == "KOR_C_SLABEDG" and len(pts(e)) >= 3]
zones = [Polygon(pts(e)).buffer(0) for e in msp.query("LWPOLYLINE POLYLINE") if e.dxf.layer == "KOR_C_SLABZONE" and len(pts(e)) >= 3]
for f in sorted(floors, key=lambda p: -p.area):
    mine = [z for z in zones if f.contains(z.representative_point())]
    if len(mine) < 2: continue
    out = sum(z.difference(f).area for z in mine)
    over = sum(z.area for z in mine) - unary_union(mine).area
    print(f"floor {f.area / SQ:,.0f} sq ft: {len(mine)} zones, sum {sum(z.area for z in mine) / SQ:,.0f}; outside the floor {out / SQ:,.1f} sq ft; zones overlapping each other {over / SQ:,.1f} sq ft")
    for i in range(len(mine)):
        for j in range(i + 1, len(mine)):
            o = mine[i].intersection(mine[j])
            if o.area / SQ > 0.5:
                c = o.representative_point()
                print(f"   zones {mine[i].area / SQ:,.0f} and {mine[j].area / SQ:,.0f} sq ft overlap {o.area / SQ:,.1f} sq ft near ({c.x:,.0f}, {c.y:,.0f});"
                      f" the smaller lies {o.area / min(mine[i].area, mine[j].area):.0%} inside the larger")
    for z in sorted(mine, key=lambda z: -z.difference(f).area)[:4]:
        d = z.difference(f)
        if d.area / SQ > 0.5:
            c = d.representative_point()
            print(f"   zone {z.area / SQ:,.0f} sq ft pokes {d.area / SQ:,.1f} sq ft outside, near ({c.x:,.0f}, {c.y:,.0f}); its outside piece's box {tuple(round(v) for v in d.bounds)}")
