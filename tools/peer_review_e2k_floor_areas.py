# JM 01819 lateral prep (row 1982, JM-LAT-PREP 14 Nov 2026; scratch e2k_floor_areas.py brought into the repo). See
# KOR.Drafter jobs/01819-01-renfrew/review/lateral/independent/LATERAL-PREP-r32.md for what it backs and its limits.
r"""Floor areas per storey, split at x = 61,000 mm, of an e2k; and which carry PART / SNOW area loads. Scratch (JM-LAT-PREP)."""
import re, sys, collections
from shapely.geometry import Polygon, box
p = sys.argv[1]; split = 61000.0
pts, areas, kind, assign, loads = {}, {}, {}, collections.defaultdict(list), collections.defaultdict(set)
for line in open(p, encoding="utf-8", errors="replace"):
    m = re.match(r'\s+POINT "([^"]+)"\s+([-\d.E+]+)\s+([-\d.E+]+)', line)
    if m: pts[m[1]] = (float(m[2]), float(m[3])); continue
    m = re.match(r'\s+AREA "([^"]+)"\s+(FLOOR|PANEL)\s+(\d+)\s+(.*)', line)
    if m: areas[m[1]] = re.findall(r'"([^"]+)"', m[4]); kind[m[1]] = m[2]; continue
    m = re.match(r'\s+AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"', line)
    if m: assign[m[2]].append(m[1]); continue
    m = re.match(r'\s+AREALOAD\s+"([^"]+)"\s+"([^"]+)".*LC "([^"]+)"', line)
    if m: loads[(m[1], m[2])].add(m[3])
W, E = box(-1e9, -1e9, split, 1e9), box(split, -1e9, 1e9, 1e9)
rows = []
for st, names in assign.items():
    a = collections.Counter()
    for n in names:
        if kind.get(n) != "FLOOR": continue
        poly = Polygon([pts[q] for q in areas[n]]).buffer(0)
        lc = loads.get((n, st), set())
        for side, half in (("W", W), ("E", E)):
            ar = poly.intersection(half).area / 1e6
            a[side] += ar
            for l in ("PART", "SNOW"):
                if l in lc: a[side + l] += ar
    rows.append((st, a))
print("storey,W_m2,E_m2,W_PART_m2,E_PART_m2,W_SNOW_m2,E_SNOW_m2")
for st, a in rows:
    print(f"{st},{a['W']:.1f},{a['E']:.1f},{a['WPART']:.1f},{a['EPART']:.1f},{a['WSNOW']:.1f},{a['ESNOW']:.1f}")
