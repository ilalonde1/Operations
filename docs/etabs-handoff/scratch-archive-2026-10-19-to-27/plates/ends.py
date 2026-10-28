import fitz, sys, math, collections
# ends.py <pdf> <page1> x0 y0 x1 y1 (page mm, top origin): every loose end of the straight linework in a window,
# with the nearest other vertex (and its degree) and the nearest other line's body. Model = paper x 100 at 1:100.
pdf, page = sys.argv[1], int(sys.argv[2])
x0, y0, x1, y1 = map(float, sys.argv[3:7])
k = 72 / 25.4
d = fitz.open(pdf)[page - 1]
segs = []
for dr in d.get_drawings():
    w = dr.get('width') or 0
    for it in dr['items']:
        pts = []
        if it[0] == 'l': pts = [(it[1], it[2])]
        elif it[0] == 're':
            r = it[1]; c = [fitz.Point(r.x0, r.y0), fitz.Point(r.x1, r.y0), fitz.Point(r.x1, r.y1), fitz.Point(r.x0, r.y1)]
            pts = [(c[i], c[(i + 1) % 4]) for i in range(4)]
        for a, b in pts:
            A = (round(a.x / k, 2), round(a.y / k, 2)); B = (round(b.x / k, 2), round(b.y / k, 2))
            if A == B: continue
            if not all(x0 <= p[0] <= x1 and y0 <= p[1] <= y1 for p in (A, B)): continue
            segs.append((A, B, round(w, 2)))
deg = collections.Counter()
for A, B, _ in segs: deg[A] += 1; deg[B] += 1
def body(p, A, B):
    dx, dy = B[0] - A[0], B[1] - A[1]; L = dx * dx + dy * dy
    t = max(0, min(1, ((p[0] - A[0]) * dx + (p[1] - A[1]) * dy) / L))
    return math.hypot(A[0] + t * dx - p[0], A[1] + t * dy - p[1])
pens = collections.Counter(w for *_, w in segs)
print(f"{len(segs)} segments; pens {dict(pens)}")
rows = []
for A, B, w in segs:
    for p, q in ((A, B), (B, A)):
        if deg[p] != 1: continue
        if math.hypot(p[0] - q[0], p[1] - q[1]) < 2: continue   # ignore stubs under 200 mm (hatch, ticks)
        nv = min(((math.hypot(v[0] - p[0], v[1] - p[1]), v) for v in deg if v != p and v != q), default=(9e9, None))
        nb = min((body(p, a, b) for a, b, _ in segs if (a, b) != (A, B)), default=9e9)
        rows.append((nb, p, w, nv[0], deg[nv[1]] if nv[1] else 0, math.hypot(p[0] - q[0], p[1] - q[1])))
for nb, p, w, nvd, nvdeg, L in sorted(rows):
    if nb > 6: continue
    print(f"end ({p[0]:7.2f},{p[1]:7.2f}) pen {w}  own {L*100/25.4:6.0f} in  body {nb*100:6.0f} mm  vertex {nvd*100:6.0f} mm (deg {nvdeg})")
