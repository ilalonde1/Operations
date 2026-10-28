import sys
import ezdxf

# valid.py <dxf>... : the slab layer's closed outlines over 1,000 sq ft - area, vertices, and every pair of
# non-adjacent edges that cross or touch (within 1 mm), which a ring must not have


def area(p):
    n = len(p)
    return abs(sum(p[i][0] * p[(i + 1) % n][1] - p[(i + 1) % n][0] * p[i][1] for i in range(n))) / 2


def seg_dist(a, b, c, d):
    def pt_seg(p, s, t):
        dx, dy = t[0] - s[0], t[1] - s[1]
        L = dx * dx + dy * dy
        u = 0 if L == 0 else max(0, min(1, ((p[0] - s[0]) * dx + (p[1] - s[1]) * dy) / L))
        return ((p[0] - s[0] - u * dx) ** 2 + (p[1] - s[1] - u * dy) ** 2) ** 0.5

    def cross(o, p, q):
        return (p[0] - o[0]) * (q[1] - o[1]) - (p[1] - o[1]) * (q[0] - o[0])
    d1, d2, d3, d4 = cross(c, d, a), cross(c, d, b), cross(a, b, c), cross(a, b, d)
    if ((d1 > 0) != (d2 > 0)) and ((d3 > 0) != (d4 > 0)):
        return 0.0
    return min(pt_seg(a, c, d), pt_seg(b, c, d), pt_seg(c, a, b), pt_seg(d, a, b))


for f in sys.argv[1:]:
    doc = ezdxf.readfile(f)
    print('==', f.replace('\\', '/').split('/')[-4])
    for e in doc.modelspace():
        if e.dxf.layer != 'KOR_C_SLABEDG':
            continue
        if e.dxftype() == 'LWPOLYLINE':
            p = [(x, y) for x, y, *_ in e.get_points()]
        elif e.dxftype() == 'POLYLINE':
            p = [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
        else:
            continue
        if len(p) < 4 or area(p) / 92903 < 1000:
            continue
        n = len(p)
        bad = []
        for i in range(n):
            for j in range(i + 2, n):
                if i == 0 and j == n - 1:
                    continue
                dd = seg_dist(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n])
                if dd < 1.0:
                    bad.append((i, j, round(dd, 2), [round(v) for v in p[i]], [round(v) for v in p[j]]))
        xs, ys = [q[0] for q in p], [q[1] for q in p]
        print(f'  {area(p) / 92903:,.0f} sq ft, {n} vertices, x {min(xs):.0f}..{max(xs):.0f} y {min(ys):.0f}..{max(ys):.0f}; '
              f'non-adjacent edges crossing or touching: {len(bad)} {bad[:6]}')
