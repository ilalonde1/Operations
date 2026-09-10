"""RENDER EVERY VIEW WHOSE SLAB EDGE DID NOT CLOSE, AND SEE WHERE IT STOPS.

One contact sheet, one cell per view: the unclaimed linework in grey, columns and walls in blue,
the longest open chains in colour, and a red ring at each chain END -- which is where the edge
stops. Built 2026-09-10, when fifteen views on 31168 were failing and fourteen of them had never
been looked at; the sheet showed at a glance that the tower views' perimeters were absent
altogether while other jobs' were merely broken, which are different faults.

    python docs/etabs-handoff/view_breaks.py <dxfFolder> <out.png> [nameSubstring...]

⚠ It draws the DXF, so a line the intake consumed (a wall's face, a slab edge) is NOT in it. An
absent perimeter here means "claimed by something else", not "not drawn" -- check pdf_lines.py.
"""

import math
import os
import sys

from PIL import Image, ImageDraw

CELL = 460
COLS = 4
PAD = 26


def polys_of(path):
    raw = open(path, encoding="utf-8", errors="replace").read().splitlines()
    out = []
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
                    out.append((plyr, pts))
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
    return out


TOL = 1.0


def chains_of(segs):
    def key(p):
        return (round(p[0] / TOL), round(p[1] / TOL))

    ends = {}
    for idx, (a, b) in enumerate(segs):
        ends.setdefault(key(a), []).append(idx)
        ends.setdefault(key(b), []).append(idx)
    seen = set()
    out = []
    for start in range(len(segs)):
        if start in seen:
            continue
        seen.add(start)
        pl = [segs[start][0], segs[start][1]]
        grew = True
        while grew:
            grew = False
            for end_i, at in ((0, pl[0]), (1, pl[-1])):
                for idx in ends.get(key(at), []):
                    if idx in seen:
                        continue
                    a, b = segs[idx]
                    nxt = b if key(a) == key(at) else (a if key(b) == key(at) else None)
                    if nxt is None:
                        continue
                    seen.add(idx)
                    pl.insert(0, nxt) if end_i == 0 else pl.append(nxt)
                    grew = True
                    break
                if grew:
                    break
        out.append(pl)
    return out


def runlen(pl):
    return sum(math.dist(a, b) for a, b in zip(pl, pl[1:]))


PALETTE = [(200, 30, 30), (20, 110, 200), (0, 150, 70), (210, 120, 0), (140, 40, 170)]


def draw_view(path):
    ps = polys_of(path)
    beam, struct = [], []
    for lyr, p in ps:
        u = lyr.upper()
        if u == "BEAM":
            for a, b in zip(p, p[1:]):
                beam.append((a, b))
        elif u.startswith("KOR_V"):
            struct.append(p)
    if not beam:
        return None
    pts = [q for s in beam for q in s] + [q for p in struct for q in p]
    X0, X1 = min(q[0] for q in pts), max(q[0] for q in pts)
    Y0, Y1 = min(q[1] for q in pts), max(q[1] for q in pts)
    span = max(X1 - X0, Y1 - Y0) or 1.0
    k = (CELL - 2 * PAD) / span

    def T(q):
        return (PAD + (q[0] - X0) * k, CELL - PAD - (q[1] - Y0) * k)

    im = Image.new("RGB", (CELL, CELL), (255, 255, 255))
    d = ImageDraw.Draw(im)
    for a, b in beam:
        d.line([T(a), T(b)], fill=(207, 207, 207), width=1)
    for p in struct:
        d.line([T(q) for q in p] + [T(p[0])], fill=(150, 185, 225), width=1)

    ch = [c for c in chains_of(beam) if math.dist(c[0], c[-1]) > TOL]
    ch.sort(key=lambda c: -runlen(c))
    for n, pl in enumerate(ch[:5]):
        col = PALETTE[n % len(PALETTE)]
        d.line([T(q) for q in pl], fill=col, width=2)
        for e in (pl[0], pl[-1]):
            x, y = T(e)
            d.ellipse([x - 4, y - 4, x + 4, y + 4], outline=(220, 0, 0), width=2)
    return im, (ch[0] if ch else None)


folder, out = sys.argv[1], sys.argv[2]
wanted = sys.argv[3:]
files = [f for f in sorted(os.listdir(folder)) if f.endswith(".dxf")
         and (not wanted or any(w in f for w in wanted))]
cells = []
for f in files:
    r = draw_view(os.path.join(folder, f))
    if r is None:
        continue
    im, longest = r
    d = ImageDraw.Draw(im)
    name = f.split("_", 2)[-1].replace(".dxf", "")[:44]
    d.rectangle([0, 0, CELL - 1, 15], fill=(255, 255, 255))
    d.text((4, 3), name, fill=(0, 0, 0))
    if longest:
        d.text((4, CELL - 14), f"longest open chain {runlen(longest):,.0f} mm, ends {math.dist(longest[0], longest[-1]):,.0f} mm apart",
               fill=(150, 0, 0))
    d.rectangle([0, 0, CELL - 1, CELL - 1], outline=(180, 180, 180))
    cells.append(im)

rows = (len(cells) + COLS - 1) // COLS
sheet = Image.new("RGB", (COLS * CELL, rows * CELL), (255, 255, 255))
for i, c in enumerate(cells):
    sheet.paste(c, ((i % COLS) * CELL, (i // COLS) * CELL))
sheet.save(out)
print(f"{out}  {len(cells)} views  {sheet.size[0]}x{sheet.size[1]}")
