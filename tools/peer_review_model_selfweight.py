# JM 01819 lateral prep (row 1982, JM-LAT-PREP 14 Nov 2026; scratch model_selfweight.py brought into the repo). See
# KOR.Drafter jobs/01819-01-renfrew/review/lateral/independent/LATERAL-PREP-r32.md for what it backs and its limits.
r"""Self-weight per storey and side (x 61,000) of a gravity e2k, lumped like the hand weight (slabs at their storey; walls and
columns half to the storey top, half to the storey below). DIAGNOSIS ONLY (JM-LAT-PREP): it is our model, never a reference."""
import re, sys, collections, csv
from shapely.geometry import Polygon, box
e2k, hand = sys.argv[1], sys.argv[2]
G = 24.0; SPLIT = 61000.0
pts, area, kind, prop_t, fsec, lines, hs, order = {}, {}, {}, {}, {}, {}, {}, []
assign = []
for l in open(e2k, encoding="utf-8", errors="replace"):
    m = re.match(r'\s+STORY "([^"]+)"\s+HEIGHT\s+([\d.E+-]+)', l)
    if m: order.append(m[1]); hs[m[1]] = float(m[2]); continue
    m = re.match(r'\s+POINT "([^"]+)"\s+([-\d.E+]+)\s+([-\d.E+]+)', l)
    if m: pts[m[1]] = (float(m[2]), float(m[3])); continue
    m = re.match(r'\s+SHELLPROP\s+"([^"]+)".*(?:SLABTHICKNESS|WALLTHICKNESS)\s+([\d.]+)', l)
    if m: prop_t[m[1]] = float(m[2]); continue
    m = re.match(r'\s+FRAMESECTION\s+"([^"]+)".*SHAPE "Concrete Rectangular"\s+D ([\d.]+) B ([\d.]+)', l)
    if m: fsec[m[1]] = float(m[2]) * float(m[3]); continue
    m = re.match(r'\s+AREA "([^"]+)"\s+(FLOOR|PANEL)\s+\d+\s+(.*)', l)
    if m: area[m[1]] = re.findall(r'"([^"]+)"', m[3]); kind[m[1]] = m[2]; continue
    m = re.match(r'\s+LINE\s+"([^"]+)"\s+(COLUMN|BEAM)\s+"([^"]+)"\s+"([^"]+)"', l)
    if m: lines[m[1]] = (m[2], m[3], m[4]); continue
    m = re.match(r'\s+(AREA|LINE)ASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION "([^"]+)"', l)
    if m: assign.append((m[1], m[2], m[3], m[4]))
below = {order[i]: (order[i + 1] if i + 1 < len(order) else None) for i in range(len(order))}
W = collections.defaultdict(lambda: collections.Counter())
def side(x): return "A" if x < SPLIT else "B"
for typ, name, st, sec in assign:
    if typ == "AREA":
        if name not in area: continue
        P = [pts[p] for p in area[name]]
        if kind[name] == "FLOOR":
            poly = Polygon(P).buffer(0)
            for b, half in (("A", box(-1e9, -1e9, SPLIT, 1e9)), ("B", box(SPLIT, -1e9, 1e9, 1e9))):
                W[(st, b)]["slab"] += poly.intersection(half).area / 1e6 * prop_t.get(sec, 0) / 1e3 * G
        else:
            xs = sorted(set(P)); (x1, y1), (x2, y2) = xs[0], xs[-1]
            L = ((x1 - x2) ** 2 + (y1 - y2) ** 2) ** .5
            w = L * prop_t.get(sec, 0) * hs[st] / 1e9 * G
            b = side((x1 + x2) / 2); W[(st, b)]["walls"] += w / 2
            if below[st]: W[(below[st], b)]["walls"] += w / 2
    else:
        if name not in lines: continue
        k, p1, p2 = lines[name]
        if k == "COLUMN":
            w = fsec.get(sec, 0) * hs[st] / 1e9 * G; b = side(pts[p1][0])
            W[(st, b)]["cols"] += w / 2
            if below[st]: W[(below[st], b)]["cols"] += w / 2
        else:
            (x1, y1), (x2, y2) = pts[p1], pts[p2]
            W[(st, side((x1 + x2) / 2))]["beams"] += ((x1 - x2) ** 2 + (y1 - y2) ** 2) ** .5 * fsec.get(sec, 0) / 1e9 * G
H = {}
for r in csv.DictReader(open(hand, encoding="utf-8")):
    H[(r["storey"], r["building"])] = r
print("storey,side,model_slab,hand_slab,model_walls,hand_walls,model_cols,hand_cols,model_beams,hand_area_m2")
for st in reversed(order):
    for b in "AB":
        c = W.get((st, b)); h = H.get((st, b))
        if not c and not h: continue
        c = c or collections.Counter(); g = (lambda k: float(h[k]) if h else 0)
        print(f"{st},{b},{c['slab']:.0f},{g('slab_kN'):.0f},{c['walls']:.0f},{g('walls_kN'):.0f},{c['cols']:.0f},{g('columns_kN'):.0f},{c['beams']:.0f},{g('slab_area_m2'):.0f}")
