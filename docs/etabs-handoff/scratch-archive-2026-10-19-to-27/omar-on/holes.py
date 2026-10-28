"""List a storey's openings as boxes in metres from the plate's lower-left. usage: holes.py <model.e2k> <storey>"""
import re, sys
pts, areas, assign = {}, {}, {}
for raw in open(sys.argv[1], encoding="latin-1"):
    s = raw.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m[1]] = (float(m[2]), float(m[3])); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+(\w+)\s+\d+\s+(.*)', s)
    if m: areas[m[1]] = (m[2], re.findall(r'"([^"]+)"', m[3])); continue
    m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"(.*)', s)
    if m and m[2] == sys.argv[2]: assign[m[1]] = m[3]
floor = [a for a in assign if a in areas and areas[a][0] == "FLOOR" and "OPENING" not in assign[a]]
fp = [pts[p] for a in floor for p in areas[a][1] if p in pts]
if not fp:
    print(sys.argv[2], "no plate"); sys.exit()
x0, y0 = min(p[0] for p in fp), min(p[1] for p in fp)
print(sys.argv[2], "plate", round((max(p[0] for p in fp) - x0) / 1000, 2), "x", round((max(p[1] for p in fp) - y0) / 1000, 2))
for a in assign:
    if a in areas and "OPENING" in assign[a]:
        q = [pts[p] for p in areas[a][1] if p in pts]
        xs, ys = [(p[0] - x0) / 1000 for p in q], [(p[1] - y0) / 1000 for p in q]
        print(f"  {a:8} x {min(xs):6.2f}-{max(xs):6.2f}  y {min(ys):6.2f}-{max(ys):6.2f}  {len(q)} pts")
