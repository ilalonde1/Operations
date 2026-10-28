r"""dxf_window.py <dxf> <x> <y> <half-size mm> <out.png> : every line and polyline of a sheet's DXF within a window, coloured
by layer - what the reader saw round one point."""
import sys
import ezdxf
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
path, cx, cy, h, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
doc = ezdxf.readfile(path)
colours = {}
fig, ax = plt.subplots(figsize=(9, 9))
inside = lambda x, y: abs(x - cx) <= h and abs(y - cy) <= h
for e in doc.modelspace():
    t = e.dxftype(); lay = e.dxf.layer
    col = colours.setdefault(lay, f"C{len(colours) % 10}")
    if t == "LINE":
        a, b = e.dxf.start, e.dxf.end
        if inside(a.x, a.y) or inside(b.x, b.y): ax.plot([a.x, b.x], [a.y, b.y], color=col, lw=1.5 if lay == "KOR_V_COL" else 0.6)
    elif t in ("LWPOLYLINE", "POLYLINE"):
        p = [(v[0], v[1]) for v in e.get_points()] if t == "LWPOLYLINE" else [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
        if any(inside(x, y) for x, y in p):
            if (e.closed if t == "LWPOLYLINE" else e.is_closed): p = p + p[:1]
            ax.plot([x for x, _ in p], [y for _, y in p], color=col, lw=2.5 if lay == "KOR_V_COL" else 0.8)
ax.plot([cx], [cy], "k+", ms=18)
ax.set_xlim(cx - h, cx + h); ax.set_ylim(cy - h, cy + h); ax.set_aspect("equal")
for lay, col in colours.items(): ax.plot([], [], color=col, label=lay)
ax.legend(fontsize=7, loc="upper right"); ax.set_title(f"{path.split(chr(92))[-1][:50]} round ({cx:.0f}, {cy:.0f})")
fig.savefig(out, dpi=80, bbox_inches="tight"); print(out)
