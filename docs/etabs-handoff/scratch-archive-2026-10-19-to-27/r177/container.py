import sys, re
# container.py <e2k> <storey> <x> <y> : the openings on the storey whose box holds the point
pts, areas = {}, {}
st, x, y = sys.argv[2], float(sys.argv[3]), float(sys.argv[4])
for line in open(sys.argv[1], encoding='utf-8', errors='replace'):
    s = line.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m.group(1)] = (float(m.group(2)), float(m.group(3))); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+\S+\s+(\d+)\s+(.*)', s)
    if m: areas[m.group(1)] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]; continue
    m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"(.*)', s)
    if m and m.group(2) == st and m.group(1) in areas:
        p = [pts[q] for q in areas[m.group(1)] if q in pts]
        xs, ys = [q[0] for q in p], [q[1] for q in p]
        if p and min(xs) <= x <= max(xs) and min(ys) <= y <= max(ys):
            kind = 'OPENING' if 'OPENING' in m.group(3) else 'plate/other'
            print(f"{m.group(1)} {kind} x {min(xs):.0f}..{max(xs):.0f} y {min(ys):.0f}..{max(ys):.0f} ({len(p)} corners)")
