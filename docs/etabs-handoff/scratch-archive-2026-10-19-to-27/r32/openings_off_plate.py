import sys, re
# openings_off_plate.py <model.e2k> <storey> : every opening on the storey, its area, and the share of its corners standing
# outside every plate of that storey - a hole has floor on every side of it, so a high share is an opening that is not one
pts, areas, assigns = {}, {}, []
for line in open(sys.argv[1], encoding='utf-8', errors='replace'):
    s = line.strip()
    m = re.match(r'POINT\s+"([^"]+)"\s+(\S+)\s+(\S+)', s)
    if m: pts[m.group(1)] = (float(m.group(2)), float(m.group(3))); continue
    m = re.match(r'AREA\s+"([^"]+)"\s+\S+\s+(\d+)\s+(.*)', s)
    if m: areas[m.group(1)] = re.findall(r'"([^"]+)"', m.group(3))[:int(m.group(2))]; continue
    m = re.match(r'AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"(.*)', s)
    if m: assigns.append((m.group(1), m.group(2), m.group(3)))


def poly(name): return [pts[p] for p in areas.get(name, []) if p in pts]


def area(p): return abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p)))) / 2


def inside(q, p):
    c = False
    for i in range(len(p)):
        a, b = p[i], p[(i + 1) % len(p)]
        if (a[1] > q[1]) != (b[1] > q[1]) and q[0] < (b[0] - a[0]) * (q[1] - a[1]) / (b[1] - a[1]) + a[0]: c = not c
    return c


st = sys.argv[2]
plates = [poly(n) for n, s, rest in assigns if s == st and 'OPENING' not in rest and n.startswith('KF')]
for n, s, rest in assigns:
    if s != st or 'OPENING' not in rest: continue
    p = poly(n)
    if not p: continue
    out = sum(1 for q in p if not any(inside(q, f) for f in plates))
    xs, ys = [q[0] for q in p], [q[1] for q in p]
    print(f"{n:8} {area(p):10.1f} {out}/{len(p)} corners off every plate  x {min(xs):.0f}..{max(xs):.0f} y {min(ys):.0f}..{max(ys):.0f}")
