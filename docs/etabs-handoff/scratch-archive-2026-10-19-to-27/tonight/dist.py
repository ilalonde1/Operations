r"""dist.py <job> <storey> <commit> : for each member the commit lost or gained on that storey - distance to the nearest of hers
(registered into our frame), to the nearest of today's of that kind, and to the nearest OTHER one of that kind in the build
before the commit (a lost column with a twin beside it was a duplicate)."""
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "member_verdicts.py"), encoding="utf-8").read()
exec(src[:src.index("for job, c, sts in ITEMS:")])
job, st, c = sys.argv[1:4]
ox, oy = offset(job)
hm = members(os.path.join(YARD, job.split("-arch")[0] + ".e2k"))
hs = her_storey(hm, st); h = hm.get(hs, {"column": [], "wall": []})
i = COMMITS.index(c); prev = f"walk-{COMMITS[i - 1]}" if i else "rb-7345"
before = members(os.path.join(SIX, job, prev, "out.e2k")).get(norm(st), {"column": [], "wall": []})
fin = members(os.path.join(SIX, job, "walk-86a2e160", "out.e2k")).get(norm(st), {"column": [], "wall": []})
nd = lambda p, pts, skip=None: min((math.hypot(p[0] - q[0], p[1] - q[1]) for q in pts if not (skip and math.hypot(p[0] - q[0], p[1] - q[1]) < 1)), default=float("inf"))
print(f"{job} {st} (hers {hs}) @ {c}")
for k, kind, x, y, ln in diff(os.path.join(SIX, job, prev, "out.e2k"), os.path.join(SIX, job, f"walk-{c}", "out.e2k")).get(st, []):
    p = (x, y)
    print(f"  {k:<6} {kind:<6} ({x:,.0f}, {y:,.0f}) {ln:,.0f}  hers {nd((x + ox, y + oy), h[kind]):,.0f} mm"
          f"  today {nd(p, fin[kind]):,.0f} mm  twin-before {nd(p, before[kind], skip=True):,.0f} mm")
