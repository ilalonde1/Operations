r"""pair_look.py <pair-dir> <job> <storey> <x> <y> [half-mm] : a moved member, LOOKED at - one storey of a gate_pair_moves.py pair
around a point, before (left) and after (right), the engineer's walls and columns in grey under both, in OUR frame.
Writes <pair-dir>\<job>\look-<storey>-<x>-<y>.png.

WHY (2026-10-27). gate_pair_moves.py judges midpoints; a wall that merged with its neighbour, or a column that became a wall in
the same place, reads as lost + gained there. ee's rule for the WallNetwork gate: a move is neither fix nor fault until it is
rendered against her model and looked at.

Registration: the after side's yardstick.txt ("column registration (dx, dy) mm") when present, else judge_drift_moves.offset.
COVERS: PANEL walls (drawn as their plan segment, width by thickness unknown - a line), LINE columns (a square marker; no
section size), on the storey named (matched as judge_drift_moves matches storeys). DOES NOT: plates, openings, sections.
SAME-CLASS FAULT IT MISSES: two walls 20 mm apart draw as one line at this scale - read the coordinates from gate_pair_moves.
Exit 0; 2 on bad arguments.
"""
import math, os, sys
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import YARD, model, norm, offset, members, storey_keys   # noqa: E402

if len(sys.argv) < 6:
    print(__doc__); sys.exit(2)
pair, job, storey = sys.argv[1:4]
cx, cy = float(sys.argv[4]), float(sys.argv[5])
half = float(sys.argv[6]) if len(sys.argv) > 6 else 3000.0


def segs(path, st, o=(0.0, 0.0)):
    """walls as plan segments and columns as points on storey st, moved by -o (her frame into ours)"""
    _, pts, cols, panels, _, _, lines = model(path)
    import re
    want = storey_keys(st)
    W, C = [], []
    for s in lines:
        m = re.match(r'\s*(LINEASSIGN|AREAASSIGN)\s+"([^"]+)"\s+"([^"]+)"', s)
        if not m or not (storey_keys(m.group(3)) & want): continue
        if m.group(1) == "AREAASSIGN" and m.group(2) in panels:
            xy = sorted({pts[q] for q in panels[m.group(2)] if q in pts})
            if len(xy) >= 2:
                a, b = max(((p, q) for p in xy for q in xy), key=lambda pq: math.dist(*pq))
                W.append(((a[0] - o[0], a[1] - o[1]), (b[0] - o[0], b[1] - o[1])))
        elif m.group(1) == "LINEASSIGN" and m.group(2) in cols:
            p = pts.get(cols[m.group(2)][0])
            if p: C.append((p[0] - o[0], p[1] - o[1]))
    return W, C


before = os.path.join(pair, job, "before", "out.e2k"); after = os.path.join(pair, job, "after", "out.e2k")
hers_path = os.path.join(YARD, job.split("-arch")[0] + ".e2k")
o = (0.0, 0.0)
if os.path.isfile(hers_path):
    o = offset(pair, job, "after", members(after), members(hers_path))
fig, axes = plt.subplots(1, 2, figsize=(14, 7))
for ax, (label, path) in zip(axes, (("before", before), ("after", after))):
    if os.path.isfile(hers_path):
        hw, hc = segs(hers_path, storey, o)
        for a, b in hw: ax.plot([a[0], b[0]], [a[1], b[1]], color="0.75", lw=6, solid_capstyle="butt", zorder=1)
        if hc: ax.scatter([p[0] for p in hc], [p[1] for p in hc], s=140, marker="s", color="0.7", zorder=1)
    w, c = segs(path, storey)
    for a, b in w:
        ax.plot([a[0], b[0]], [a[1], b[1]], color="#b22222", lw=2, zorder=2)
        ax.scatter([a[0], b[0]], [a[1], b[1]], s=10, color="#b22222", zorder=3)
    if c: ax.scatter([p[0] for p in c], [p[1] for p in c], s=60, marker="s", facecolors="none", edgecolors="#2e7d32", linewidths=2, zorder=3)
    ax.plot([cx], [cy], marker="+", color="k", ms=14, zorder=4)
    ax.set_xlim(cx - half, cx + half); ax.set_ylim(cy - half, cy + half); ax.set_aspect("equal")
    ax.set_title(f"{job} {storey} {label}: red walls, green columns; grey = the engineer's", fontsize=9)
out = os.path.join(pair, job, f"look-{norm(storey)}-{int(cx)}-{int(cy)}.png")
fig.tight_layout(); fig.savefig(out, dpi=90); print(out, f"registration {o[0]:,.0f}, {o[1]:,.0f}")
