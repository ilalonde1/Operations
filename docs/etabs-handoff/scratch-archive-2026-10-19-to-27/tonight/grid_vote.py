r"""grid_vote.py <e2k> <engineer.e2k> <dx,dy>: for each near-miss pair the stack check judged (WRONG TWIN: the neighbour is
right; PARTNER: this column is right), which of the two stands nearer the model's own grid (sum of its distances to the
nearest X and Y grid line) - does the grid pick the engineer's?"""
import math, re, sys
sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools\CorpusGateBisect")
from column_stack_near_miss import columns_by_storey, near_misses
from judge_drift_moves import members, norm

e2k, yard, off = sys.argv[1], sys.argv[2], sys.argv[3]
dx, dy = map(float, off.split(","))
gx, gy = [], []
for s in open(e2k, encoding="utf-8", errors="replace"):
    if m := re.match(r'\s*GRID\s+"[^"]+"\s+LABEL\s+"[^"]+"\s+DIR\s+"(X|Y)"\s+COORD\s+(\S+)', s):
        (gx if m.group(1) == "X" else gy).append(float(m.group(2)))
g = lambda p: min((abs(p[0] - v) for v in gx), default=0) + min((abs(p[1] - v) for v in gy), default=0)
order, at = columns_by_storey(e2k)
hers = members(yard)
right = wrong = tie = 0
for st, p, d in near_misses(order, at):
    i = order.index(st)
    nbs = [(order[j], q) for j in (i - 1, i + 1) if 0 <= j < len(order) for q in at.get(order[j], []) if 100 <= math.hypot(p[0] - q[0], p[1] - q[1]) <= 700]
    if not nbs: continue
    nst, q = min(nbs, key=lambda t: math.hypot(p[0] - t[1][0], p[1] - t[1][1]))
    hs = norm(st)
    if hs not in hers: continue
    dp = min((math.hypot(p[0] + dx - h[0], p[1] + dy - h[1]) for h in hers[hs]["column"]), default=1e9)
    if dp > 1000: continue
    truth = "partner-right" if abs(dp - d) <= 150 and dp > 150 else "this-right" if dp <= 150 else "?"
    if truth == "?": continue
    gp, gq = g(p), g(q)
    pick = "this" if gp < gq - 25 else "partner" if gq < gp - 25 else "tie"
    ok = (pick == "this" and truth == "this-right") or (pick == "partner" and truth == "partner-right")
    right += ok; tie += pick == "tie"; wrong += (not ok) and pick != "tie"
    print(f"{st} ({p[0]:.0f} {p[1]:.0f}) grid {gp:.0f} | {nst} ({q[0]:.0f} {q[1]:.0f}) grid {gq:.0f} -> grid picks {pick}; truth {truth}")
print(f"grid picks the engineer's: {right}; picks the other: {wrong}; cannot tell (within 25 mm): {tie}")
