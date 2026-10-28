"""Per storey: each floor plate's box (m), and columns outside every plate. usage: plates.py <model.e2k>"""
import re, sys, collections
pts, areas, lines = {}, {}, {}
area_storey, line_storey = collections.defaultdict(list), collections.defaultdict(list)
for raw in open(sys.argv[1], encoding="latin-1"):
    s = raw.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m[1]] = (float(m[2]) / 1000, float(m[3]) / 1000); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+FLOOR\s+\d+\s+(.*)', s)
    if m: areas[m[1]] = re.findall(r'"([^"]+)"', m[2]); continue
    m = re.match(r'LINE\s+"([^"]+)"\s+COLUMN\s+"([^"]+)"', s)
    if m: lines[m[1]] = m[2]; continue
    m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"', s)
    if m and m[1] in areas: area_storey[m[2]].append(m[1]); continue
    m = re.match(r'LINEASSIGN\s+"([^"]+)"\s+"([^"]+)"', s)
    if m and m[1] in lines: line_storey[m[2]].append(m[1])

def inside(p, poly):
    x, y = p; c = False
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i], poly[i - 1]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1: c = not c
    return c

order = list(dict.fromkeys(list(area_storey) + list(line_storey)))
for st in order:
    polys = [[pts[i] for i in areas[a] if i in pts] for a in area_storey[st]]
    boxes = ["x %.1f-%.1f y %.1f-%.1f" % (min(p[0] for p in q), max(p[0] for p in q), min(p[1] for p in q), max(p[1] for p in q)) for q in polys]
    cols = [pts[lines[c]] for c in line_storey[st] if lines[c] in pts]
    out = [c for c in cols if not any(inside(c, q) for q in polys)]
    print(f"{st:5} plates {len(polys)}: {' | '.join(boxes)}   columns {len(cols)}, outside every plate {len(out)}")
