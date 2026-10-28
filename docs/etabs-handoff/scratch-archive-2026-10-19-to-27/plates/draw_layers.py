import sys, ezdxf, matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
# draw_layers.py <out.png> <dxf>... : every line/polyline coloured by layer, one panel per dxf
out, files = sys.argv[1], sys.argv[2:]
colors = {'KOR_C_SLABEDG': 'red', 'BEAM': '#4a90d9', 'KOR_V-WALL': 'black', 'KOR_V_COL': 'green', 'GRID': '#cccccc'}
fig, axes = plt.subplots(1, len(files), figsize=(11 * len(files), 11))
if len(files) == 1: axes = [axes]
for ax, f in zip(axes, files):
    doc = ezdxf.readfile(f)
    for e in doc.modelspace():
        c = colors.get(e.dxf.layer, 'orange')
        lw = 1.6 if e.dxf.layer == 'KOR_C_SLABEDG' else 0.5
        if e.dxftype() == 'LINE':
            ax.plot([e.dxf.start.x, e.dxf.end.x], [e.dxf.start.y, e.dxf.end.y], color=c, lw=lw)
        elif e.dxftype() == 'LWPOLYLINE':
            p = [(x, y) for x, y, *_ in e.get_points()]
            if e.closed: p.append(p[0])
            ax.plot([q[0] for q in p], [q[1] for q in p], color=c, lw=lw)
        elif e.dxftype() == 'POLYLINE':
            p = [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
            if e.is_closed: p.append(p[0])
            ax.plot([q[0] for q in p], [q[1] for q in p], color=c, lw=lw)
    ax.set_aspect('equal'); ax.set_title(f.replace('\\', '/').split('/')[-1][:70], fontsize=10)
fig.text(0.01, 0.01, 'red = slab edge layer, blue = BEAM, black = walls, green = columns, orange = other', fontsize=10)
plt.tight_layout(); plt.savefig(out, dpi=70)
