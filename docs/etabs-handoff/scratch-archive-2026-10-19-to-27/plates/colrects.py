import sys, math, ezdxf, collections
# colrects.py <dxf> : every closed polyline on a column layer - its two side lengths and the bearing of its long side
doc = ezdxf.readfile(sys.argv[1])
rows = collections.Counter()
for e in doc.modelspace():
    if 'COL' not in e.dxf.layer.upper(): continue
    if e.dxftype() == 'LWPOLYLINE': p = [(x, y) for x, y, *_ in e.get_points()]
    elif e.dxftype() == 'POLYLINE': p = [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
    else: continue
    if len(p) != 4: continue
    a = math.dist(p[0], p[1]); b = math.dist(p[1], p[2])
    i, j = (0, 1) if a >= b else (1, 2)
    bearing = math.degrees(math.atan2(p[j][1] - p[i][1], p[j][0] - p[i][0])) % 180
    rows[(round(min(a, b)), round(max(a, b)), round(bearing))] += 1
for (s, l, br), n in sorted(rows.items(), key=lambda r: -r[1])[:25]:
    print(f"{n:4}  {s} x {l} mm, long side at {br} deg")
