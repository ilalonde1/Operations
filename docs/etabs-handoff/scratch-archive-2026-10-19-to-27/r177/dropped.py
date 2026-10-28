import sys, re, math, collections
# dropped.py <off.e2k> <on.e2k> <hers.e2k> <dx> <dy> : openings in off that on lacks (by storey and centre within 50 mm),
# each with its size and the nearest of her openings on the storey (ours shifted by dx, dy), and the openings on keeps there
TO_MM = {'MM': 1.0, 'IN': 25.4, 'FT': 304.8, 'M': 1000.0}


def openings(path):
    u = 1.0; pts = {}; areas = {}; out = []
    for line in open(path, encoding='utf-8', errors='replace'):
        s = line.strip()
        if s.startswith('UNITS'): u = TO_MM.get(re.findall(r'"([^"]+)"', s)[1].upper(), 1.0)
        m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
        if m: pts[m.group(1)] = (float(m.group(2)) * u, float(m.group(3)) * u); continue
        m = re.match(r'AREA\s+"([^"]+)"\s+\S+\s+(\d+)\s+(.*)', s)
        if m: areas[m.group(1)] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]; continue
        m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)".*OPENING', s)
        if m and m.group(1) in areas:
            p = [pts[q] for q in areas[m.group(1)] if q in pts]
            if not p: continue
            xs, ys = [q[0] for q in p], [q[1] for q in p]
            out.append((m.group(2), (sum(xs) / len(xs), sum(ys) / len(ys)), max(xs) - min(xs), max(ys) - min(ys), m.group(1)))
    return out


off, on, hers = openings(sys.argv[1]), openings(sys.argv[2]), openings(sys.argv[3])
dx, dy = float(sys.argv[4]), float(sys.argv[5])
for st, c, w, h, n in off:
    if any(o[0] == st and math.dist(o[1], c) < 50 for o in on): continue
    near = min((x for x in hers if x[0] == st), key=lambda x: math.dist(x[1], (c[0] + dx, c[1] + dy)), default=None)
    kept = [o for o in on if o[0] == st and math.dist(o[1], c) < 3000]
    hd = f"hers {near[4]} {near[2]:.0f}x{near[3]:.0f} at {math.dist(near[1], (c[0] + dx, c[1] + dy)):.0f} mm" if near else "none of hers"
    print(f"{st:5} {n:7} {w:5.0f}x{h:<5.0f} at ({c[0]:.0f},{c[1]:.0f}); {hd}; on keeps near it: " + ", ".join(f"{o[4]} {o[2]:.0f}x{o[3]:.0f}" for o in kept))
