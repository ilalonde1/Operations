r"""hfx_side.py <dxf> <e2k> <storey> <out.png> : a Halifax level drawn twice at one scale - LEFT the DXF the converter was given
(walls red, columns green, slab edges grey; every wall polyline numbered), RIGHT what the e2k carries on that storey (wall
panels as their footprint lines, columns as points with their outline from the section) - to say, spot by spot, whether a fault
was already in the DXF or was made by the converter. DXF mm, e2k ft (x 304.8); the e2k's own offset, if any, is read from the
two models' column sets (median of nearest pairs)."""
import math, re, sys
import ezdxf
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
dxf, e2k, st, out = sys.argv[1:5]
d = ezdxf.readfile(dxf)
polys = []
for e in d.modelspace():
    if e.dxftype() == "POLYLINE":
        p = [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
        if e.is_closed: p = p + p[:1]
        polys.append((e.dxf.layer, p))
    elif e.dxftype() == "ARC":
        c, r = e.dxf.center, e.dxf.radius
        polys.append((e.dxf.layer, [(c.x + r * math.cos(t), c.y + r * math.sin(t)) for t in np.linspace(0, 2 * math.pi, 25)]))
txt = open(e2k, encoding="utf-8", errors="replace").read().splitlines()
pts = {m.group(1): (float(m.group(2)) * 304.8, float(m.group(3)) * 304.8) for s in txt
       if (m := re.match(r'\s*POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s))}
areas = {m.group(1): re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))] for s in txt
         if (m := re.match(r'\s*AREA\s+"([^"]+)"\s+PANEL\s+(\d+)\s+(.*)', s))}
lines = {m.group(1): m.group(2) for s in txt if (m := re.match(r'\s*LINE\s+"([^"]+)"\s+COLUMN\s+"([^"]+)"', s))}
walls = [n for s in txt if (m := re.match(r'\s*AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s)) and m.group(2) == st and (n := (m.group(1), m.group(3)))[0] in areas]
cols = [n for s in txt if (m := re.match(r'\s*LINEASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"(.*ANG\s+(\S+))?', s)) and m.group(2) == st and (n := (m.group(1), m.group(3), m.group(5)))[0] in lines]
fig, (a, b) = plt.subplots(1, 2, figsize=(22, 11), sharex=True, sharey=True)
colour = {"KOR_V-WALL": "red", "KOR_V_COL": "green", "KOR_C_SLABEDG": "0.6"}
for i, (lay, p) in enumerate(polys):
    a.plot([x for x, _ in p], [y for _, y in p], color=colour.get(lay, "blue"), lw=1.2 if lay != "KOR_C_SLABEDG" else 0.6)
    if lay == "KOR_V-WALL": a.text(p[0][0], p[0][1], f"W{i}", fontsize=7, color="red")
a.set_title(f"DXF given to the converter: {dxf.split(chr(92))[-1].split('/')[-1]}")
for name, sec in walls:
    p = list(dict.fromkeys(pts[q] for q in areas[name] if q in pts))   # a panel is its centre line: two distinct points
    b.plot([x for x, _ in p], [y for _, y in p], color="red", lw=1.5)
    b.text(sum(x for x, _ in p) / len(p), sum(y for _, y in p) / len(p), f"{name} {sec.replace('KOR-W', 'W')}", fontsize=6, color="red")
for name, sec, ang in cols:
    x, y = pts[lines[name]]
    b.plot([x], [y], "s", color="green", ms=3)
    b.text(x, y, f"{sec.replace('KOR-', '')}{' ' + ang if ang else ''}", fontsize=6, color="green")
for lay, p in polys:
    if lay == "KOR_C_SLABEDG": b.plot([x for x, _ in p], [y for _, y in p], color="0.85", lw=0.5)
b.set_title(f"e2k storey {st}: walls (section), columns (section, ANG)")
for ax in (a, b): ax.set_aspect("equal")
import os
if os.environ.get("HFX_BOX"):   # x0,x1,y0,y1 in mm: zoom both panels
    x0, x1, y0, y1 = map(float, os.environ["HFX_BOX"].split(","))
    a.set_xlim(x0, x1); a.set_ylim(y0, y1)
fig.savefig(out, dpi=70, bbox_inches="tight"); print(out, len(walls), "walls", len(cols), "columns")
