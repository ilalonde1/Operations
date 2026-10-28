r"""where126.py <off-work> <e1-work> <job> : for one set, every plate/wall/column that differs between the step-off and the
126 build, with its storey, centroid (mm) and whether it lies inside a frame box the 126 build took (from its report)."""
import collections, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "diff126.py"), encoding="utf-8").read()
exec(src[:src.index("h = lambda p")])          # TAKE, num, takes(), geometry(), unit_mm()
off_w, e1_w, job = sys.argv[1:4]
po, pe = os.path.join(off_w, job, "out.e2k"), os.path.join(e1_w, job, "out.e2k")
go, ge = geometry(po), geometry(pe)
u = unit_mm(po)
boxes = []
for st, area, sheet, x, y, kind in takes(e1_w, job):
    side = (num(area) * 92903.04) ** 0.5
    boxes.append((st, x * u, y * u, num(area), sheet[:30]))
print("taken:", [(b[0], round(b[1]), round(b[2]), b[3]) for b in boxes])
mid = lambda k: (sum(p[0] for p in k[2]) / len(k[2]), sum(p[1] for p in k[2]) / len(k[2]))
for label, c in (("in off, not in 126", go - ge), ("in 126, not in off", ge - go)):
    print(label + ":")
    for k in c.elements():
        m = mid(k)
        xs, ys = [p[0] for p in k[2]], [p[1] for p in k[2]]
        print(f"   {k[0]:6} {k[1]:6} centre ({m[0]:,.0f}, {m[1]:,.0f})  box x {min(xs):,.0f}..{max(xs):,.0f} y {min(ys):,.0f}..{max(ys):,.0f}")
