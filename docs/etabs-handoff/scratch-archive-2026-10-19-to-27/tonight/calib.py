r"""calib.py : per item storey, how many of OUR columns land on hers (the classifier's reach)."""
import os, re, math
src = open("member_verdicts.py", encoding="utf-8").read()
exec(src[:src.index("for job, c, sts in ITEMS:")])
for job, c, sts in ITEMS:
    hers = members(os.path.join(YARD, job.split("-arch")[0] + ".e2k"))
    ox, oy = offset(job)
    b = members(os.path.join(SIX, job, f"walk-{c}", "out.e2k"))
    sc = lambda sg: sum(near((x + sg * ox, y + sg * oy), hers[s]["column"], 100) for s in b for x, y in b[s]["column"] if s in hers)
    sg = 1 if sc(1) >= sc(-1) else -1
    for st in sts.split(","):
        n = norm(st); ours = b.get(n, {"column": []})["column"]; h = hers.get(n, {"column": []})["column"]
        on = sum(near((x + sg * ox, y + sg * oy), h, 300) for x, y in ours)
        print(f"{job} {st} @ {c}: sign {sg:+d}; {on} of {len(ours)} of our columns land on one of her {len(h)}")
