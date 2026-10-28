"""Every closed polyline in a view DXF: layer, box (m), area (sq ft). usage: rings.py <view.dxf>"""
import sys, ezdxf
doc = ezdxf.readfile(sys.argv[1])
for e in doc.modelspace():
    if e.dxftype() not in ("POLYLINE", "LWPOLYLINE"): continue
    pts = [(v[0], v[1]) for v in e.points()]
    if len(pts) < 3: continue
    a = abs(sum(pts[i][0] * pts[i - 1][1] - pts[i - 1][0] * pts[i][1] for i in range(len(pts)))) / 2
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    if a / 92903.04 < 30: continue
    print(f"{e.dxf.layer:24} x {min(xs)/1000:5.1f}-{max(xs)/1000:5.1f} y {min(ys)/1000:5.1f}-{max(ys)/1000:5.1f}  {a/92903.04:7.0f} sq ft  {len(pts)} pts")
