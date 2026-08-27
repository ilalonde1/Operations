import io, re, sys

def load(path):
    pts, rings = {}, []
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
                rings.append((m.group(1), ring))
    return rings

def seg_cross(p1, p2, p3, p4):
    def o(a, b, c):
        v = (b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0])
        return 0 if abs(v) < 1e-9 else (1 if v > 0 else -1)
    o1, o2, o3, o4 = o(p1,p2,p3), o(p1,p2,p4), o(p3,p4,p1), o(p3,p4,p2)
    return o1 != o2 and o3 != o4 and o1 and o2 and o3 and o4

for path in sys.argv[1:]:
    bad, dup = [], []
    for name, ring in load(path):
        n = len(ring)
        # coincident consecutive points
        for i in range(n):
            a, b = ring[i], ring[(i+1) % n]
            if abs(a[0]-b[0]) < 1e-6 and abs(a[1]-b[1]) < 1e-6:
                dup.append(name); break
        crossed = False
        for i in range(n):
            for j in range(i+2, n):
                if i == 0 and j == n-1: continue
                if seg_cross(ring[i], ring[(i+1)%n], ring[j], ring[(j+1)%n]):
                    crossed = True; break
            if crossed: break
        if crossed: bad.append(name)
    print(f'{path.split("/")[-1]}')
    print(f'   self-intersecting: {len(bad)}   {bad[:8]}')
    print(f'   coincident points: {len(dup)}   {dup[:8]}')
