r"""twin_sources.py <build-dir> <x,y> [...] : the sheet DXFs whose KOR_V_COL outlines are centred within 60 mm of each point,
with the outline's size - which sheet drew a WRONG TWIN, and which drew the stack it misses."""
import glob, math, os, sys
import ezdxf
d, pts = sys.argv[1], [tuple(map(float, a.split(","))) for a in sys.argv[2:]]
hits = {p: [] for p in pts}
for f in sorted(glob.glob(os.path.join(d, "dxf", "*.dxf"))):
    try: doc = ezdxf.readfile(f)
    except Exception: continue
    for e in doc.modelspace().query('*[layer=="KOR_V_COL"]'):
        t = e.dxftype()
        if t == "LWPOLYLINE": p = [(v[0], v[1]) for v in e.get_points()]
        elif t == "POLYLINE": p = [(v.dxf.location.x, v.dxf.location.y) for v in e.vertices]
        elif t == "CIRCLE":
            c, r = e.dxf.center, e.dxf.radius; p = [(c.x - r, c.y - r), (c.x + r, c.y + r)]
        else: continue
        xs, ys = [a for a, _ in p], [b for _, b in p]
        c = ((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2)
        for q in pts:
            if math.hypot(c[0] - q[0], c[1] - q[1]) < 60:
                hits[q].append(f"{os.path.basename(f)[:46]} {max(xs) - min(xs):.0f}x{max(ys) - min(ys):.0f}")
for q, h in hits.items():
    print(f"({q[0]:.0f}, {q[1]:.0f}): {len(h)} sheet(s)")
    for s in h[:6]: print(f"    {s}")
