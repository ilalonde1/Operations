import io, re, statistics, sys

path = sys.argv[1]
pts = {}
areas = []
for line in io.open(path, encoding='utf-8', errors='replace'):
    m = re.match(r'\s*POINT\s+"([^"]+)"\s+(-?[\d.]+)\s+(-?[\d.]+)', line)
    if m:
        pts[m.group(1)] = (float(m.group(2)), float(m.group(3)))
        continue
    m = re.match(r'\s*AREA\s+"(K[FO]\d+)"\s+(FLOOR|AREA)\s+(\d+)\s+(.*)$', line)
    if m:
        names = re.findall(r'"([^"]+)"', m.group(4))[: int(m.group(3))]
        ring = [pts[n] for n in names if n in pts]
        if len(ring) < 3:
            continue
        s = 0.0
        for i in range(len(ring)):
            x1, y1 = ring[i]
            x2, y2 = ring[(i + 1) % len(ring)]
            s += x1 * y2 - x2 * y1
        areas.append((m.group(1), abs(s) / 2.0 / 144.0))   # sq in -> sq ft

floors = [a for n, a in areas if n.startswith('KF')]
holes = [a for n, a in areas if n.startswith('KO')]
floors.sort()
print('plates      :', len(floors))
print('median sq ft:', round(statistics.median(floors)))
print('total sq ft :', round(sum(floors)))
print('smallest    :', round(floors[0]), ' largest:', round(floors[-1]))
print('openings    :', len(holes), ' total sq ft:', round(sum(holes)) if holes else 0)
