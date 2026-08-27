import io, re, sys

def load(path):
    pts, floors, holes, story = {}, [], [], {}
    assign = {}
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'\s*POINT\s+"([^"]+)"\s+(-?[\d.]+)\s+(-?[\d.]+)', line)
        if m:
            pts[m.group(1)] = (float(m.group(2)), float(m.group(3)))
            continue
        m = re.match(r'\s*AREA\s+"(K[FO]\d+)"\s+(?:FLOOR|AREA)\s+(\d+)\s+(.*)$', line)
        if m:
            names = re.findall(r'"([^"]+)"', m.group(3))[: int(m.group(2))]
            ring = [pts[n] for n in names if n in pts]
            if len(ring) >= 3:
                (floors if m.group(1).startswith('KF') else holes).append((m.group(1), ring))
            continue
        m = re.match(r'\s*AREAASSIGN\s+"(K[FO]\d+)"\s+"([^"]+)"', line)
        if m:
            assign[m.group(1)] = m.group(2)
    return floors, holes, assign

def area(ring):
    s = 0.0
    for i in range(len(ring)):
        x1, y1 = ring[i]
        x2, y2 = ring[(i + 1) % len(ring)]
        s += x1 * y2 - x2 * y1
    return abs(s) / 2.0 / 144.0

def centroid(ring):
    return (sum(p[0] for p in ring) / len(ring), sum(p[1] for p in ring) / len(ring))

def inside(pt, ring):
    x, y = pt
    c = False
    n = len(ring)
    for i in range(n):
        x1, y1 = ring[i]
        x2, y2 = ring[(i + 1) % n]
        if ((y1 > y) != (y2 > y)) and (x < (x2 - x1) * (y - y1) / (y2 - y1 + 1e-12) + x1):
            c = not c
    return c

for path in sys.argv[1:]:
    floors, holes, assign = load(path)
    print('=' * 90)
    print(path.split('/')[-1], f'-- {len(floors)} plates, {len(holes)} openings')
    worst = []
    for hn, hr in holes:
        ha = area(hr)
        hc = centroid(hr)
        host = None
        for fn, fr in floors:
            if assign.get(fn) == assign.get(hn) and inside(hc, fr):
                if host is None or area(fr) < area(host[1]):
                    host = (fn, fr)
        if host is None:
            worst.append((ha, hn, assign.get(hn, '?'), 'NO PLATE AROUND IT', 0.0))
        else:
            fa = area(host[1])
            worst.append((ha, hn, assign.get(hn, '?'), host[0], ha / fa))
    worst.sort(reverse=True)
    for ha, hn, st, host, ratio in worst[:6]:
        print(f'   {hn:8} {st:14} {ha:9,.0f} sq ft   in {host:20} {ratio*100:5.1f}% of it')
    orph = [w for w in worst if w[3] == 'NO PLATE AROUND IT']
    big = [w for w in worst if w[4] > 0.5]
    print(f'   orphans: {len(orph)}   openings over half their plate: {len(big)}')
