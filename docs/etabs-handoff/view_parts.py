"""What one view DXF holds, as objects: every wall panel with its thickness and length.

    python docs/etabs-handoff/view_parts.py <view.dxf> [layerSubstring=WALL]

Use it to ask whether a "wall" is really a wall. On 31168's tower views this is what showed four
of the fourteen panels were 711 mm x 3,633 mm and 264 mm x 4,828 mm strips lying exactly on the
plan's outer extent -- the floor's own slab edge, read as a wall (intake step 28).
"""

import sys

path = sys.argv[1]
raw = open(path, encoding="utf-8", errors="replace").read().splitlines()
polys = []
i = 0
cur = pts = plyr = vx = None
while i < len(raw) - 1:
    c, v = raw[i].strip(), raw[i + 1].strip()
    if c == "0":
        if v == "POLYLINE":
            cur, pts, plyr = "P", [], None
        elif v == "VERTEX":
            cur, vx = "V", None
        elif v == "SEQEND":
            if plyr and pts:
                polys.append((plyr, pts))
            cur = None
        i += 2
        continue
    if cur == "P" and c == "8":
        plyr = v
    if cur == "V":
        if c == "10":
            vx = float(v)
        if c == "20":
            pts.append((vx, float(v)))
    i += 2

want = sys.argv[2] if len(sys.argv) > 2 else "WALL"
sel = [(l, p) for l, p in polys if want.upper() in l.upper()]
print(f"{len(sel)} '{want}' panels")
rows = []
for l, p in sel:
    xs = [q[0] for q in p]
    ys = [q[1] for q in p]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    thick, length = (w, h) if w < h else (h, w)
    rows.append((min(xs), min(ys), max(xs), max(ys), thick, length))
rows.sort(key=lambda r: -r[5])
for x0, y0, x1, y1, t, L in rows:
    print(f"  thick {t:7.0f} mm ({t/25.4:5.1f} in)  length {L:8.0f} mm ({L/25.4:6.1f} in)   "
          f"bbox ({x0:8.0f},{y0:8.0f}) .. ({x1:8.0f},{y1:8.0f})")
