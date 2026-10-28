"""For one storey: each column's position (m), whether it stands in a floor plate on its own storey, and whether a column
continues from the same spot on the storey above. usage: colstack.py <model.e2k> <storey> <storey above>"""
import re, sys
pts, areas, lines, a_st, l_st = {}, {}, {}, {}, {}
for raw in open(sys.argv[1], encoding="latin-1"):
    s = raw.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m[1]] = (float(m[2]) / 1000, float(m[3]) / 1000); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+FLOOR\s+\d+\s+(.*)', s)
    if m: areas[m[1]] = re.findall(r'"([^"]+)"', m[2]); continue
    m = re.match(r'LINE\s+"([^"]+)"\s+COLUMN\s+"([^"]+)"', s)
    if m: lines[m[1]] = m[2]; continue
    m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"(.*)', s)
    if m and m[1] in areas and "OPENING" not in m[3]: a_st.setdefault(m[2], []).append(m[1])
    m = re.match(r'LINEASSIGN\s+"([^"]+)"\s+"([^"]+)"', s)
    if m and m[1] in lines: l_st.setdefault(m[2], []).append(m[1])
def inside(p, poly):
    c = False
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i], poly[i - 1]
        if (y1 > p[1]) != (y2 > p[1]) and p[0] < (x2 - x1) * (p[1] - y1) / (y2 - y1) + x1: c = not c
    return c
st, up = sys.argv[2], sys.argv[3]
plates = [[pts[j] for j in areas[a] if j in pts] for a in a_st.get(st, [])]
above = [pts[lines[l]] for l in l_st.get(up, []) if lines[l] in pts]
for l in l_st.get(st, []):
    p = pts.get(lines[l])
    if p is None: continue
    on = any(inside(p, q) for q in plates)
    cont = any(abs(p[0] - q[0]) < 0.3 and abs(p[1] - q[1]) < 0.3 for q in above)
    print(f"{l:8} ({p[0]:6.2f},{p[1]:6.2f})  floor at its top: {'yes' if on else 'NO '}  continues to {up}: {'yes' if cont else 'no'}")
