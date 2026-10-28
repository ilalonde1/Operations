"""Print the floor plates on a storey: their points in metres. usage: plate.py <model.e2k> <storey>"""
import re, sys
pts, areas, st = {}, {}, {}
for s in open(sys.argv[1], encoding="latin-1"):
    s = s.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m[1]] = (float(m[2]), float(m[3])); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+FLOOR\s+\d+\s+(.*)', s)
    if m: areas[m[1]] = re.findall(r'"([^"]+)"', m[2]); continue
    m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"', s)
    if m and m[1] in areas: st.setdefault(m[1], []).append(m[2])
for a, js in areas.items():
    if sys.argv[2] in st.get(a, []):
        print(a, st[a][:5], len(js), "points")
        print([tuple(round(v / 1000, 2) for v in pts[j]) for j in js if j in pts])
