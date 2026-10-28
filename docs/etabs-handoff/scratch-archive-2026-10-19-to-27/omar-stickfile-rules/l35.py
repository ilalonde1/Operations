import re, collections
exec(open("plates.py").read().split("def inside")[0].replace("sys.argv[1]", "'out.e2k'"))
def inside(p, poly):
    x, y = p; c = False
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i], poly[i - 1]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1: c = not c
    return c
st = "L35"
polys = [[pts[i] for i in areas[a] if i in pts] for a in area_storey[st]]
for c in line_storey[st]:
    p = pts[lines[c]]
    if not any(inside(p, q) for q in polys):
        print(f"{c} {p[0]*1000:.0f} {p[1]*1000:.0f}")
