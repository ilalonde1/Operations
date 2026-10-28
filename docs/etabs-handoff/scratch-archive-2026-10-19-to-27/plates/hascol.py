import sys, re, math
# hascol.py <hers.e2k> <storey> <dx> <dy> x,y [x,y ...] : for each of our points (shifted by dx, dy into her frame), her
# nearest column on that storey and how far it is
TO_MM = {'MM': 1.0, 'IN': 25.4, 'FT': 304.8, 'M': 1000.0}
u = 1.0; pts = {}; cols = {}; on = []
for line in open(sys.argv[1], encoding='utf-8', errors='replace'):
    s = line.strip()
    if s.startswith('UNITS'): u = TO_MM.get(re.findall(r'"([^"]+)"', s)[1].upper(), 1.0)
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u); continue
    m = re.match(r'LINE\s+"([^"]+)"\s+COLUMN\s+"([^"]+)"', s)
    if m: cols[m.group(1)] = m.group(2); continue
    m = re.match(r'LINEASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION\s+"([^"]+)"', s)
    if m and m.group(2) == sys.argv[2] and m.group(1) in cols and cols[m.group(1)] in pts:
        on.append((pts[cols[m.group(1)]], m.group(3)))
dx, dy = float(sys.argv[3]), float(sys.argv[4])
print(f"her columns on {sys.argv[2]}: {len(on)}")
for a in sys.argv[5:]:
    x, y = (float(v) for v in a.split(','))
    q = (x + dx, y + dy)
    near = min(on, key=lambda c: math.dist(c[0], q), default=None)
    print(f"  ours ({x:.0f}, {y:.0f}): " + (f"hers {near[1]} at {math.dist(near[0], q):.0f} mm" if near else "none"))
