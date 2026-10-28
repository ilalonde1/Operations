"""Draw one view DXF by layer: wall-markup red, wall orange, slab edge blue, column green, beam light grey."""
import sys, ezdxf
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
doc = ezdxf.readfile(sys.argv[1])
style = {"KOR_V-WALL-MARKUP": ("red", 2.0), "KOR_V-WALL": ("darkorange", 1.2), "KOR_C_SLABEDG": ("royalblue", 0.8),
         "KOR_C_SLABEDG-MARKUP": ("cyan", 1.5), "KOR_V_COL": ("green", 2.0), "BEAM": ("#dddddd", 0.3)}
fig, ax = plt.subplots(figsize=(7, 16), dpi=90)
count = {}
for e in doc.modelspace():
    if e.dxftype() not in ("POLYLINE", "LWPOLYLINE", "LINE"): continue
    layer = e.dxf.layer
    colour, width = style.get(layer, ("#bbbbbb", 0.3))
    pts = [(e.dxf.start.x, e.dxf.start.y), (e.dxf.end.x, e.dxf.end.y)] if e.dxftype() == "LINE" else [(v[0], v[1]) for v in e.points()]
    if e.dxftype() != "LINE" and e.is_closed: pts.append(pts[0])
    ax.plot([p[0] for p in pts], [p[1] for p in pts], color=colour, linewidth=width)
    count[layer] = count.get(layer, 0) + 1
ax.set_aspect("equal"); ax.axis("off")
x0, x1 = float(sys.argv[3]), float(sys.argv[4]); ax.set_xlim(x0, x1)
fig.tight_layout(); fig.savefig(sys.argv[2])
print(count)
