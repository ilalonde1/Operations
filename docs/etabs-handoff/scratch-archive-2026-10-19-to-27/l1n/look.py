r"""Draw one sheet's slab-edge linework, every 'SLAB' text, and the nearest-call-out cells over the largest closed slab
polyline, so the step-182 shares can be judged by eye. COVERS: LWPOLYLINE/LINE on *SLAB* layers, TEXT/MTEXT with SLAB.
DOES NOT: block inserts, the classifier's own ring walk (the cells are drawn over the bounding box of all slab linework)."""
import re, sys
import ezdxf
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

doc = ezdxf.readfile(sys.argv[1])
msp = doc.modelspace()
fig, ax = plt.subplots(figsize=(16, 13))
xs, ys = [], []
for e in msp:
    lay = e.dxf.layer.upper()
    if "SLAB" not in lay:
        continue
    if e.dxftype() == "LINE":
        p, q = e.dxf.start, e.dxf.end
        ax.plot([p.x, q.x], [p.y, q.y], "k-", lw=0.6); xs += [p.x, q.x]; ys += [p.y, q.y]
    elif e.dxftype() in ("LWPOLYLINE", "POLYLINE"):
        pts = [(p[0], p[1]) for p in (e.get_points() if e.dxftype() == "LWPOLYLINE" else e.points())]
        if e.is_closed: pts.append(pts[0])
        a = np.array(pts); ax.plot(a[:, 0], a[:, 1], "k-", lw=0.6); xs += list(a[:, 0]); ys += list(a[:, 1])
tags = []
for e in msp.query("TEXT MTEXT"):
    t = e.plain_text() if e.dxftype() == "MTEXT" else e.dxf.text
    if "SLAB" in t.upper():
        m = re.search(r"(\d+)", t)
        p = e.dxf.insert
        tags.append((p.x, p.y, t.replace("\n", " ")[:20], int(m.group(1)) if m else None))
cols = {int(k): c for k, c in (a.split(":", 1) for a in (sys.argv[3] if len(sys.argv) > 3 else "300:tab:blue,500:tab:green,600:tab:orange,900:tab:red").split(","))}
x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
gx, gy = np.meshgrid(np.linspace(x0, x1, 200), np.linspace(y0, y1, 200))
withnum = [t for t in tags if t[3] in cols]
d = np.stack([np.hypot(gx - t[0], gy - t[1]) for t in withnum])
near = np.argmin(d, axis=0)
img = np.zeros(gx.shape + (4,))
for i, t in enumerate(withnum):
    c = matplotlib.colors.to_rgba(cols[t[3]], 0.18)
    img[near == i] = c
ax.imshow(img, extent=(x0, x1, y0, y1), origin="lower", aspect="equal")
for x, y, s, n in tags:
    ax.plot(x, y, "o", color=cols.get(n, "m"), ms=7)
    ax.annotate(s, (x, y), fontsize=8, color=cols.get(n, "m"), xytext=(4, 4), textcoords="offset points")
ax.set_aspect("equal"); ax.set_title(sys.argv[1].split("\\")[-1][:90], fontsize=9)
plt.savefig(sys.argv[2], dpi=110, bbox_inches="tight")
print(len(tags), "SLAB texts:", [(s, round(x), round(y)) for x, y, s, n in tags])
