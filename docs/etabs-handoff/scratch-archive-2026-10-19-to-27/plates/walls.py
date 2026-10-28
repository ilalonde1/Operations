import sys, re, math, collections
# walls.py <model.e2k>: wall volume by section thickness, and overlapping walls (same storey, collinear, overlapping
# by more than 300 mm) - the two ways a takeoff could over-count walls. Model in mm.
e = open(sys.argv[1], encoding='utf-8', errors='replace').read()
pts = {m[0]: (float(m[1]), float(m[2])) for m in re.findall(r'POINT "(\w+)"\s+(-?[\d.]+)\s+(-?[\d.]+)', e)}
h = {}
for m in re.findall(r'STORY "([^"]+)"\s+HEIGHT\s+([\d.]+)', e): h[m[0]] = float(m[1])
area = {m[0]: (m[1], m[2]) for m in re.findall(r'AREA "(\w+)"\s+PANEL\s+4\s+"(\w+)"\s+"(\w+)"', e)}
walls = []
for name, storey, sec in re.findall(r'AREAASSIGN\s+"(\w+)"\s+"([^"]+)"\s+SECTION "KOR-W([\d.]+)"', e):
    if name not in area: continue
    a, b = pts[area[name][0]], pts[area[name][1]]
    walls.append((storey, a, b, float(sec)))
byT = collections.Counter(); vol = 0
for s, a, b, t in walls:
    v = math.dist(a, b) * t * h.get(s, 0) / 1e9; byT[t] += v; vol += v
print(f"{len(walls)} walls, {vol:,.0f} m3 at full storey height")
for t, v in sorted(byT.items()): print(f"  {t:7.1f} mm thick: {v:7.0f} m3")
over = 0; n = 0
by = collections.defaultdict(list)
for w in walls: by[w[0]].append(w)
for s, ws in by.items():
    for i in range(len(ws)):
        for j in range(i + 1, len(ws)):
            _, a, b, t = ws[i]; _, c, d, u = ws[j]
            L = math.dist(a, b)
            if L == 0: continue
            ux, uy = (b[0] - a[0]) / L, (b[1] - a[1]) / L
            off = lambda p: abs((p[0] - a[0]) * uy - (p[1] - a[1]) * ux)
            if off(c) > (t + u) / 2 or off(d) > (t + u) / 2: continue
            t0 = (c[0] - a[0]) * ux + (c[1] - a[1]) * uy; t1 = (d[0] - a[0]) * ux + (d[1] - a[1]) * uy
            ov = min(L, max(t0, t1)) - max(0, min(t0, t1))
            if ov > 300:
                n += 1; over += ov * min(t, u) * h.get(s, 0) / 1e9
print(f"overlapping wall pairs: {n}, the overlap {over:,.0f} m3")
