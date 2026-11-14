"""Hand seismic weight W per storey per building (VBBL 2019 4.1.8.2 = NBC 2015), row 1982 JM-LAT-PREP.

Independent of the ETABS model: reads ONLY
  plates.json               slab plates measured off WHM's GA plans by peer_review_hand_weight_plates.py (PDF vector linework)
  model-audit/drawn-walls.csv, drawn-columns.csv, drawn-openings.csv, slab-outlines.csv, drawn-uses.csv
  answer-key.csv            A3.01 floor-to-floor, S111 SDL / snow, WHM slab-thickness notes (read by hand)
No *.e2k, composed*.json, ETABS/SAFE output or yardsticks W is read. The yardsticks' W is printed for comparison only.
Run: python -I peer_review_hand_weight.py   (KOR_HW_DIR as for the plates; in the repo since JM-LAT-PREP 14 Nov 2026, was hw_weight.py)
"""
import csv, json, math, os
from collections import defaultdict
from shapely.geometry import Polygon, box, LineString, Point
from shapely.ops import unary_union

JOB = r"C:\VIsual Studio Projects\KOR.Drafter\jobs\01819-01-renfrew"
MA = os.path.join(JOB, "model-audit")
HW = os.environ.get("KOR_HW_DIR", os.path.join(JOB, "review", "lateral", "independent"))   # reads plates.json, writes hand-weight.csv here
GC = 24.0          # kN/m3 normal-weight reinforced concrete (WHM: 'REGULAR WEIGHT', no density stated)
IN = 0.0254
SPLIT = 61000.0
S_SNOW = 1.64      # S111
FT = 0.3048


def rows(path):
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(l for l in f if not l.startswith("#")))


def ring(s):
    return Polygon([tuple(map(float, p.split())) for p in s.split(";")])


def ftin(s):  # 9'-6" -> m
    s = s.replace('"', "").strip()
    f, i = s.split("'-")
    return (int(f) + float(i) / 12) * FT


# ---- storey ladder (ETABS names) and A3.01 floor-to-floor (storey = the slab at its top) ----
LADDER = ["P1/GF", "L1", "L2", "L3"] + [f"L{i}" for i in range(4, 32)] + ["ROOF AMENITY", "ROOF", "MECHANICAL SERVICE", "ELEVATOR PENTHOUSE"]
ak = list(csv.DictReader(open(os.path.join(JOB, "answer-key.csv"), encoding="utf-8")))
A_NAME = {"L2- BLDG A": "L2", "ROOF AMENITY - BLDG A": "L9", "US/ROOF - BLD A": "L10", "T.O. ELEVATOR PENTHOUSE - BLDG A": "L11"}
B_NAME = {"ROOF AMENITY -BUILD B": "ROOF AMENITY", "T.O ROOF AMENITY": "ROOF", "T.O MECHANICAL SERVICE": "MECHANICAL SERVICE",
          "T.O ELEVATOR PENTHOUSE - BLD B": "ELEVATOR PENTHOUSE"}
H = {"A": {}, "B": {}}
for r in ak:
    if r["source_file"] == "storeys" and r["item"] == "floor-to-floor" and r["building"] in ("A", "B"):
        top = r["level_to"]
        nm = (A_NAME if r["building"] == "A" else B_NAME).get(top, top)
        H[r["building"]][nm] = ftin(r["value"])
# check every storey we need has a height
for b, need in (("A", ["P1/GF", "L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8", "L9", "L10", "L11"]), ("B", LADDER)):
    miss = [s for s in need if s not in H[b]]
    assert not miss, (b, miss)


def below(s):
    i = LADDER.index(s)
    return LADDER[i - 1] if i > 0 else "P2"


# ---- slab plates: which plan draws which storey ----
plates = json.load(open(os.path.join(HW, "plates.json")))
PG = {"S205": 8, "S207": 10, "S209": 12, "S211": 14, "S213": 16, "S215": 18, "S217A": 20, "S217B": 20, "S217C": 20, "S219": 22, "S220": 23,
      "S221": 24, "S222": 25, "S223": 26, "S224": 27, "S225": 28, "S226": 29, "S227A": 30, "S227B": 30}
PLAN_OF = {}
for pid, st in [("S205", ["P1/GF"]), ("S207", ["L1"]), ("S209", ["L2"]), ("S211", ["L3"]), ("S213", ["L4"]),
                ("S215", ["L5", "L6", "L7", "L8"]), ("S217A", ["L9"]), ("S217B", ["L10"]), ("S217C", ["L11"]),
                ("S219", [f"L{i}" for i in range(10, 15)]), ("S220", [f"L{i}" for i in range(15, 27)]), ("S221", ["L27"]),
                ("S222", ["L28"]), ("S223", ["L29"]), ("S224", ["L30", "L31"]), ("S225", ["ROOF AMENITY"]), ("S226", ["ROOF"]),
                ("S227A", ["MECHANICAL SERVICE"]), ("S227B", ["ELEVATOR PENTHOUSE"])]:
    for s in st:
        PLAN_OF.setdefault(s, []).append(pid)

WEST = box(-1e6, -1e6, SPLIT, 1e6); EAST = box(SPLIT, -1e6, 1e6, 1e6)
openings = defaultdict(list)
for r in rows(os.path.join(MA, "drawn-openings.csv")):
    openings[r["storey"]].append(ring(r["ring"]))

plate = {}       # (storey, bldg) -> polygon (net of drawn openings)
plate_gross = {}
plate_src = {}
for s, pids in PLAN_OF.items():
    polys = [Polygon(p["ext"], p["holes"]) for pid in pids for p in plates[pid]["polys"]]
    g = unary_union(polys)
    op = unary_union(openings.get(s, [])) if openings.get(s) else None
    for b, side in (("A", WEST), ("B", EAST)):
        part = g.intersection(side)
        if part.area < 1e6:
            continue
        plate_gross[(s, b)] = part
        plate[(s, b)] = part.difference(op) if op is not None else part
        plate_src[(s, b)] = "+".join(f"{pid} (IFT p{PG[pid]} 0-based)" for pid in pids)

# ---- typical slab thickness (WHM notes, answer-key slabs rows) ----
def t_typ(s, b):
    if s in ("P1/GF", "L1"): return 10, "S205/S207 '10\" SLAB TYP. U.N.O.'"
    if s == "L2": return 8, "S209 local '8\" SLAB' callouts only (no TYP note; ASSUMED plate-wide)"
    if b == "A" and s == "L9": return 10, "S217 '10\" SLAB TYP. U.N.O.' (Bldg A)"
    if b == "A" and s == "L11": return 10, "S217 C '8\" THK. SLAB SLOPING UP TO 12\" MAX' -> mean 10"
    if s == "ROOF AMENITY": return 10, "S225 '10\" SLAB TYP. U.N.O.'"
    if s == "ROOF": return 11, "S226 '9\" THK. SLAB SLOPING UP TO 13\" MAX' -> mean 11"
    if s in ("MECHANICAL SERVICE", "ELEVATOR PENTHOUSE"): return 9, "S227 '9\" SLAB' (MSR) / '9\" SLAB TYP. U.N.O.' (EPH roof)"
    return 8, "S211-S224 '8\" SLAB TYP. U.N.O.'"

zones = defaultdict(list)
for r in rows(os.path.join(MA, "slab-outlines.csv")):
    zones[r["storey"]].append((ring(r["ring"]), float(r["thickness_in"]), r["zone"], r["sheet"]))

# Bldg A L10 pop-up roofs (S217 B): per-plate mean of WHM's sloping notes, by position
def popup_t(poly):
    c = poly.centroid
    if c.x < 30000: return 11, "west pop-up 10->12 in"
    if c.x < 47000: return 12, "east pop-up 10->14 in"
    return 10, "stair pop-up 8->12 in"

# ---- walls and columns: weight per ETABS storey (element spans from the level below to that storey) ----
cols = rows(os.path.join(MA, "drawn-columns.csv"))
walls = rows(os.path.join(MA, "drawn-walls.csv"))
col_xy = defaultdict(list)
for c in cols:
    col_xy[c["etabs_storey"]].append((float(c["x_mm"]), float(c["y_mm"])))

def bldg(x): return "A" if x < SPLIT else "B"

elem = defaultdict(lambda: {"walls": 0.0, "cols": 0.0})   # (etabs storey, bldg) -> kN of the element storey
drop_compact = 0; keep_compact = 0; wall_n = 0
wall_polys = defaultdict(list)
for w in walls:
    x1, y1, x2, y2 = (float(w[k]) for k in ("x1", "y1", "x2", "y2"))
    L = math.hypot(x2 - x1, y2 - y1) / 1000; t = float(w["thk_mm"]) / 1000
    mx, my = (x1 + x2) / 2, (y1 + y2) / 2
    if "COMPACT" in w["how"]:
        d = min([math.hypot(mx - x, my - y) for x, y in col_xy.get(w["storey"], [])] or [1e9])
        if d < 400:
            drop_compact += 1; continue      # the same poche is a row of drawn-columns.csv
        keep_compact += 1
    b = bldg(mx)
    s = w["storey"]
    elem[(s, b)]["walls"] += L * t * H[b][s] * GC
    wall_polys[s].append(LineString([(x1, y1), (x2, y2)]).buffer(float(w["thk_mm"]) / 2 + 25, cap_style=2) if L > 0 else Point(mx, my).buffer(t * 500))
    wall_n += 1
col_in_wall = 0
for c in cols:
    s = c["etabs_storey"]; x, y = float(c["x_mm"]), float(c["y_mm"])
    if any(p.contains(Point(x, y)) for p in wall_polys.get(s, [])):
        col_in_wall += 1; continue           # inside a counted wall poche
    b = bldg(x)
    elem[(s, b)]["cols"] += float(c["b_mm"]) / 1000 * float(c["d_mm"]) / 1000 * H[b][s] * GC

# ---- SDL by S111 use ----
SDL = {"RES": 1.2, "BAL": 1.5, "COR": 0.25, "GF": 0.5, "ROOF": 1.0, "MECH": 1.9}
def main_use(s, b):
    if s == "P1/GF": return "GF", "S111 GROUND FLOOR 0.5 kPa (A1.1.04 'GROUND FLOOR & PARKING LEVEL 1')"
    if b == "A" and s == "L9": return "BAL", "S111 BALCONIES, PATIOS AND DECKS 1.5 kPa (A1.1.12 'ROOF TOP AMENITY PLAN - BUILDING A'; pavers) ASSUMED"
    if b == "A" and s in ("L10", "L11"): return "ROOF", "S111 ROOF 1.0 kPa (Bldg A pop-up / elevator roofs)"
    if s == "ROOF AMENITY": return "BAL", "S111 BALCONIES, PATIOS AND DECKS 1.5 kPa (A1.1.24 'ROOF AMENITY FLOOR PLAN', outdoor deck) ASSUMED"
    if s in ("ROOF", "MECHANICAL SERVICE", "ELEVATOR PENTHOUSE"): return "ROOF", "S111 ROOF 1.0 kPa"
    return "RES", "S111 (IFT p3) RESIDENTIAL 1.2 kPa (A1.1.05-A1.1.23 floor plans)" + (" ASSUMED for L1-L3" if s in ("L1", "L2", "L3") else "")
uses = defaultdict(lambda: defaultdict(float))
for r in rows(os.path.join(MA, "drawn-uses.csv")):
    k = "BAL" if r["use"].startswith("BALCON") else ("COR" if r["use"].startswith("STAIRS") else None)
    if k and r["building"] in ("A", "B"):
        uses[(r["storey"], r["building"])][k] += float(r["area_m2"])

PART = set("P1/GF L1 L2 L3 L4 L5 L6 L7 L8 L9 L10 L11 L12 L13 L14 L15 L16 L17 L18 L19 L20 L21 L22 L23 L24 L25 L26 L27 L28 L29 L30 L31".split()) | {"ROOF AMENITY"}
PART_A = set("P1/GF L1 L2 L3 L4 L5 L6 L7 L8 L9".split())   # A's L10/L11 are its roofs
SNOW = {("ROOF", "B"), ("ELEVATOR PENTHOUSE", "B"), ("L9", "A"), ("L10", "A"), ("L11", "A")}

out = []
keys = sorted(set(plate) | set((s, b) for (s, b) in elem), key=lambda k: (LADDER.index(k[0]) if k[0] in LADDER else -1, k[1]))
for (s, b) in keys:
    if s not in LADDER:
        continue
    P = plate.get((s, b))
    area = P.area / 1e6 if P is not None else 0.0
    t, tsrc = t_typ(s, b)
    slab = area * t * IN * GC
    zsrc = []
    if P is not None and b == "A" and s == "L10":
        slab = 0.0
        for g in (P.geoms if hasattr(P, "geoms") else [P]):
            tt, why = popup_t(g); slab += g.area / 1e6 * tt * IN * GC; zsrc.append(f"{why} {g.area/1e6:.1f} m2")
        tsrc = "S217 B pop-up roof notes, mean of min/max"
    elif P is not None:
        for (zr, tz, zn, zs) in zones.get(s, []):
            if s == "L10":
                continue                      # L10 rows are the pop-up roofs handled above
            a = zr.intersection(P).area / 1e6
            if a > 0.05:
                slab += a * (tz - t) * IN * GC; zsrc.append(f"{zn} {a:.1f} m2 {tz:g}in")
        if b == "A" and s == "L9":           # open roof deck 10 -> 14 in sloping (mean 12) outside the indoor amenity (pop-up roofs above)
            ind = unary_union([Polygon(p["ext"]) for p in plates["S217B"]["polys"]] + [Polygon(p["ext"]) for p in plates["S217C"]["polys"]])
            deck = P.difference(ind).area / 1e6
            slab += deck * (12 - 10) * IN * GC; zsrc.append(f"open deck {deck:.1f} m2 10->14in sloping, mean 12in (S217 A note)")
    # walls/columns: half of the storey below this level (elements of storey s), half of the storey above (elements of the next storey)
    up = LADDER[LADDER.index(s) + 1] if LADDER.index(s) + 1 < len(LADDER) else None
    wk = 0.5 * elem[(s, b)]["walls"] + (0.5 * elem[(up, b)]["walls"] if up else 0)
    ck = 0.5 * elem[(s, b)]["cols"] + (0.5 * elem[(up, b)]["cols"] if up else 0)
    mu, musrc = main_use(s, b)
    u = uses.get((s, b), {})
    a_bal = min(u.get("BAL", 0), area); a_cor = min(u.get("COR", 0), area - a_bal) if mu == "RES" else 0
    if mu != "RES": a_bal = 0
    sdl = (area - a_bal - a_cor) * SDL[mu] + a_bal * SDL["BAL"] + a_cor * SDL["COR"]
    part = 0.5 * area if (s in (PART_A if b == "A" else PART)) else 0.0
    snow = 0.25 * S_SNOW * area if (s, b) in SNOW else 0.0
    W = slab + wk + ck + sdl + part + snow
    src = (f"slab {plate_src.get((s,b),'-')} outline (hw_slabs.py) less drawn-openings.csv; t {t}in {tsrc}; zones slab-outlines.csv: "
           + ("; ".join(zsrc) or "none") + f" | walls drawn-walls.csv x A3.01 floor-to-floor (arch p73) x 24 kN/m3, half storeys {s}/{up} | cols drawn-columns.csv same"
           + f" | SDL {musrc}" + (f"; drawn-uses balconies {a_bal:.1f} m2 1.5, corridors {a_cor:.1f} m2 0.25" if (a_bal or a_cor) else "")
           + (" | partition 0.5 kPa VBBL 4.1.8.2 (basis.txt partition_storeys)" if part else "")
           + (" | snow 0.25 x S 1.64 kPa (S111 p3)" if snow else ""))
    out.append(dict(storey=s, building=b, at_base="yes" if s == "P1/GF" else "", slab_area_m2=round(area, 1), slab_kN=round(slab), walls_kN=round(wk),
                    columns_kN=round(ck), sdl_kN=round(sdl), partition_kN=round(part), snow_kN=round(snow), W_kN=round(W), source=src))

with open(os.path.join(HW, "hand-weight.csv"), "w", newline="", encoding="utf-8") as f:
    wr = csv.DictWriter(f, fieldnames=list(out[0])); wr.writeheader(); wr.writerows(out)

def tot(b, key="W_kN", base=False):
    return sum(r[key] for r in out if r["building"] == b and (base or r["at_base"] != "yes"))
print(f"compact wall rows dropped as drawn columns: {drop_compact}; compact kept as piers: {keep_compact}; walls counted {wall_n} of {len(walls)}; columns inside a wall poche dropped {col_in_wall} of {len(cols)}")
for b in "AB":
    print(b, "above P1/GF:", {k: tot(b, k) for k in ("slab_kN", "walls_kN", "columns_kN", "sdl_kN", "partition_kN", "snow_kN", "W_kN")},
          "area", round(sum(r['slab_area_m2'] for r in out if r['building'] == b and r['at_base'] != 'yes')))
L1g = unary_union([Polygon(p["ext"], p["holes"]) for p in plates["S207"]["polys"]])
L1n = L1g.difference(unary_union(openings["L1"]))
print("L1 S207 slab area gross west/east of x=61000:", round(L1g.intersection(WEST).area / 1e6, 1), round(L1g.intersection(EAST).area / 1e6, 1),
      " net of drawn openings:", round(L1n.intersection(WEST).area / 1e6, 1), round(L1n.intersection(EAST).area / 1e6, 1))
ya = {"A": (90393, 93154), "B": (208966, 217618)}
for b in "AB":
    w = tot(b); wn = w - tot(b, "partition_kN")
    print(f"{b}: hand W {w} (no partitions {wn}) vs yardsticks {ya[b][1]} ({ya[b][0]}): {100*(w/ya[b][1]-1):+.1f} % ({100*(wn/ya[b][0]-1):+.1f} %)")
