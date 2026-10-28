"""Closed shapes on each layer of a view DXF: count, and centres (page mm) of the column-layer ones. usage: cols.py <view.dxf>"""
import sys, collections, ezdxf
doc = ezdxf.readfile(sys.argv[1])
by = collections.Counter()
centres = []
for e in doc.modelspace():
    if e.dxftype() not in ("POLYLINE", "LWPOLYLINE"): continue
    by[e.dxf.layer] += 1
    if "COL" in e.dxf.layer or "WALL" in e.dxf.layer:
        pts = [(v[0], v[1]) for v in e.points()]
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        if max(xs) - min(xs) < 1500 and max(ys) - min(ys) < 1500:
            centres.append((e.dxf.layer, round(sum(xs) / len(xs)), round(sum(ys) / len(ys))))
print(dict(by))
for c in sorted(centres): print("  ", c)
