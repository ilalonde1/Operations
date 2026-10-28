import fitz, sys, math
# near.py <pdf> <page1> <x_mm> <y_mm_from_top> <radius_mm>: every drawn segment near a page point, with its pen
pdf, page, x, y, r = sys.argv[1], int(sys.argv[2]), *map(float, sys.argv[3:6])
k = 72 / 25.4
px, py, pr = x * k, y * k, r * k
d = fitz.open(pdf)[page - 1]
def dist(p, a, b):
    ax, ay, bx, by = a.x, a.y, b.x, b.y
    dx, dy = bx - ax, by - ay
    L = dx * dx + dy * dy
    t = 0 if L == 0 else max(0, min(1, ((p[0] - ax) * dx + (p[1] - ay) * dy) / L))
    return math.hypot(ax + t * dx - p[0], ay + t * dy - p[1])
for i, dr in enumerate(d.get_drawings()):
    for it in dr['items']:
        if it[0] == 'l':
            a, b = it[1], it[2]
        elif it[0] == 're':
            continue
        else:
            continue
        if dist((px, py), a, b) <= pr:
            print(f"path {i:5d} w={dr.get('width') or 0:.2f}pt col={dr.get('color')} dash={dr.get('dashes')}  "
                  f"({a.x / k:7.2f},{a.y / k:7.2f})-({b.x / k:7.2f},{b.y / k:7.2f}) mm")
