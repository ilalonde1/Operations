r"""nearmiss.py <e2k> [x,y ...]: columns whose nearest column on the storey above or below is 100-700 mm away with none
within 50 mm - a stack that misses itself. Lists them per storey, and for each given point says whether a near-miss is there."""
import math, os, re, sys
sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools\CorpusGateBisect")
from judge_drift_moves import model   # noqa: E402

path = sys.argv[1]
_, pts, cols, _, _, _, lines = model(path)
order = []
for s in lines:
    if m := re.match(r'\s*STORY\s+"([^"]+)"', s): order.append(m.group(1))
at = {}
for s in lines:
    if (m := re.match(r'\s*LINEASSIGN\s+"([^"]+)"\s+"([^"]+)"', s)) and m.group(1) in cols:
        p = pts.get(cols[m.group(1)][0])
        if p: at.setdefault(m.group(2), []).append(p)
miss = []
for i, st in enumerate(order):
    nb = [order[j] for j in (i - 1, i + 1) if 0 <= j < len(order) and order[j] in at]
    for p in at.get(st, []):
        ds = [min(math.hypot(p[0] - q[0], p[1] - q[1]) for q in at[n]) for n in nb if at[n]]
        if ds and min(ds) > 50 and any(100 <= d <= 700 for d in ds):
            miss.append((st, p, min(ds)))
print(f"{len(miss)} near-miss column(s) of {sum(len(v) for v in at.values())}")
by = {}
for st, p, d in miss: by.setdefault(st, []).append((p, d))
for st in order:
    if st in by: print(f"  {st}: {len(by[st])}  " + " ".join(f"({p[0]:.0f} {p[1]:.0f})~{d:.0f}" for p, d in by[st]))
for a in sys.argv[2:]:
    x, y = map(float, a.split(","))
    hit = [m for m in miss if math.hypot(m[1][0] - x, m[1][1] - y) <= 800]
    print(f"  probe ({x:,.0f},{y:,.0f}): {'NEAR-MISS ' + str([(s, round(d)) for s, _, d in hit]) if hit else 'none'}")
