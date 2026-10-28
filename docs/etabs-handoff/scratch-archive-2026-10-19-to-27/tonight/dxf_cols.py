r"""dxf_cols.py <dxf> : the column outlines on KOR_V_COL (closed polylines and circles) as centre, width x depth (mm)."""
import sys
import ezdxf
doc = ezdxf.readfile(sys.argv[1])
out = []
for e in doc.modelspace().query("*[layer=='KOR_V_COL']"):
    if e.dxftype() == "LWPOLYLINE":
        p = [(v[0], v[1]) for v in e.get_points()]
    elif e.dxftype() == "POLYLINE":
        p = [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
    elif e.dxftype() == "CIRCLE":
        c = e.dxf.center; r = e.dxf.radius
        out.append((c.x, c.y, 2 * r, 2 * r, "round")); continue
    else:
        continue
    xs, ys = [a for a, _ in p], [b for _, b in p]
    out.append(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, max(xs) - min(xs), max(ys) - min(ys), e.dxftype()))
for x, y, w, d, k in sorted(out):
    print(f"{x:.0f} {y:.0f} {w:.0f}x{d:.0f} {k}")
print(f"{len(out)} column outline(s)", file=sys.stderr)
