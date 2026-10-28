"""Every wall panel on a storey: axis ends (m), section. usage: walls.py <model.e2k> <storey>
A stack-merged wall carries ONE label on every storey it stands on, so assignments are kept per (label, storey)."""
import re, sys
pts, areas, asg = {}, {}, {}
for s in open(sys.argv[1], encoding="latin-1"):
    s = s.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m[1]] = (float(m[2]) / 1000, float(m[3]) / 1000); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+PANEL\s+\d+\s+(.*)', s)
    if m: areas[m[1]] = re.findall(r'"([^"]+)"', m[2]); continue
    m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s)
    if m and m[1] in areas: asg[(m[1], m[2])] = (m[2], m[3])
rows = []
for (a, _), (st, sec) in asg.items():
    if st != sys.argv[2]: continue
    p = [pts[j] for j in areas[a][:2]]
    rows.append((round(min(p[0][0], p[1][0]), 2), round(min(p[0][1], p[1][1]), 2), a, p, sec))
for x, y, a, p, sec in sorted(rows):
    print(f"  {a:6} ({p[0][0]:6.2f},{p[0][1]:6.2f})-({p[1][0]:6.2f},{p[1][1]:6.2f})  {sec}")
print(len(rows), "walls")
