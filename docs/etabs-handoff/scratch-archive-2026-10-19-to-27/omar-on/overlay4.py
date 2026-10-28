"""Draw one storey of the model onto its sheet: openings blue, slab edge cyan, walls red, columns green.
usage: overlay.py <model.e2k> <storey> <page index> <out.png>
Model mm -> page: the storey's plate box is matched to the sheet's (measured: page plate x 51.8-64.6 m)."""
import re, sys, fitz
model, storey, page_ix, out = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
pts, areas, lines, assign = {}, {}, {}, {}
for raw in open(model, encoding="latin-1"):
    s = raw.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m[1]] = (float(m[2]), float(m[3])); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+(\w+)\s+\d+\s+(.*)', s)
    if m: areas[m[1]] = (m[2], re.findall(r'"([^"]+)"', m[3])); continue
    m = re.match(r'LINE\s+"([^"]+)"\s+(\w+)\s+"([^"]+)"\s+"([^"]+)"', s)
    if m: lines[m[1]] = (m[2], m[3], m[4]); continue
    m = re.match(r'(AREAASSIGN|LINEASSIGN)\s+"([^"]+)"\s+"([^"]+)"(.*)', s)
    if m and m[3] == storey: assign[m[2]] = m[4]
floor = [a for a in assign if a in areas and areas[a][0] == "FLOOR" and "OPENING" not in assign[a]]
holes = [a for a in assign if a in areas and "OPENING" in assign[a]]
walls = [a for a in assign if a in areas and areas[a][0] == "PANEL"]
cols = [l for l in assign if l in lines and lines[l][0] == "COLUMN"]
fp = [pts[p] for a in floor for p in areas[a][1] if p in pts]
mx0, my0 = min(p[0] for p in fp), min(p[1] for p in fp)
dx, dy = 51800 - mx0, 9100 - my0
doc = fitz.open(r"C:\Users\ilalonde\Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
pg = doc[page_ix]; k = 96 * 25.4 / 72; H = pg.rect.height
def P(p): return fitz.Point((p[0] + dx) / k, H - (p[1] + dy) / k)
def ring(names, colour, width):
    for a in names:
        q = [P(pts[p]) for p in areas[a][1] if p in pts]
        if len(q) >= 2: pg.draw_polyline(q + [q[0]], color=colour, width=width)
ring(floor, (0, 0.7, 0.9), 1.5)
[pg.add_polygon_annot([P(pts[p]) for p in areas[a][1] if p in pts]).update() for a in holes]
for a in walls:
    q = [P(pts[p]) for p in areas[a][1] if p in pts]
    if len(q) >= 2:
        an = pg.add_line_annot(q[0], q[1]); an.set_colors(stroke=(1, 0.85, 0)); an.set_border(width=4); an.update()
for c in cols:
    pc = P(pts[lines[c][1]]); an = pg.add_circle_annot(fitz.Rect(pc.x - 7, pc.y - 7, pc.x + 7, pc.y + 7)); an.set_colors(stroke=(0, 0.9, 0)); an.set_border(width=3); an.update()
print(f"{storey}: {len(floor)} plate(s), {len(holes)} opening(s), {len(walls)} wall panel(s), {len(cols)} column(s); shift {dx/1000:.2f},{dy/1000:.2f} m")
r = pg.rect
pg.get_pixmap(clip=fitz.Rect(r.width * 0.55, r.height * 0.13, r.width * 0.80, r.height * 0.88), dpi=80).save(out)
