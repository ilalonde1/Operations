r"""overlay.py <job> <our storey> <her storey> <out.png> [commit]: one plot - the engineer's columns and wall midpoints (grey,
moved into our frame by model-yardstick's registration), the bank's (blue) and today's (red) columns and walls, and the members
the given commit lost (x) / gained (+). So a lost or gained member can be seen against what she modelled, not counted."""
import os, sys, math
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "member_verdicts.py"), encoding="utf-8").read()
exec(src[:src.index("for job, c, sts in ITEMS:")])
job, st, hst, out = sys.argv[1:5]
c = sys.argv[5] if len(sys.argv) > 5 else None
ox, oy = offset(job)
hers = members(os.path.join(YARD, job.split("-arch")[0] + ".e2k")).get(norm(hst), {"column": [], "wall": []})
bank = members(os.path.join(SIX, job, "rb-7345", "out.e2k")).get(norm(st), {"column": [], "wall": []})
fin = members(os.path.join(SIX, job, "walk-86a2e160", "out.e2k")).get(norm(st), {"column": [], "wall": []})
fig, ax = plt.subplots(figsize=(16, 12))
hx = lambda pts: ([p[0] - ox for p in pts], [p[1] - oy for p in pts])          # hers into our frame
ax.scatter(*hx(hers["column"]), s=220, facecolors="none", edgecolors="0.55", linewidths=2, label=f"hers columns ({len(hers['column'])})")
ax.scatter(*hx(hers["wall"]), s=120, marker="s", facecolors="none", edgecolors="0.75", label=f"hers wall mids ({len(hers['wall'])})")
ax.scatter([p[0] for p in bank["column"]], [p[1] for p in bank["column"]], s=60, c="tab:blue", label=f"bank columns ({len(bank['column'])})")
ax.scatter([p[0] for p in fin["column"]], [p[1] for p in fin["column"]], s=18, c="tab:red", label=f"today columns ({len(fin['column'])})")
if c:
    i = COMMITS.index(c); prev = f"walk-{COMMITS[i - 1]}" if i else "rb-7345"
    for k, kind, x, y, ln in diff(os.path.join(SIX, job, prev, "out.e2k"), os.path.join(SIX, job, f"walk-{c}", "out.e2k")).get(st, []):
        ax.scatter([x], [y], s=300, marker="x" if k == "LOST" else "+", c="k" if k == "LOST" else "tab:green", linewidths=2.5)
        ax.annotate(f"{k[0]} {kind[0]}", (x, y), textcoords="offset points", xytext=(6, 6), fontsize=8)
ax.set_aspect("equal"); ax.legend(loc="upper left", fontsize=9)
ax.set_title(f"{job} {st} (hers {hst}) - x = lost, + = gained at {c or '-'}; grey = engineer, shifted by registration")
fig.savefig(out, dpi=80, bbox_inches="tight"); print(out)
