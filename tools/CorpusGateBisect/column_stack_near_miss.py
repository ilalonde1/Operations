r"""column_stack_near_miss.py <model.e2k> [<engineer.e2k> <dx,dy>] : columns whose stack misses itself.

A column whose nearest column on the storey directly above or below stands 100-700 mm away, with none within 50 mm, is a
NEAR-MISS: a stack that should be one line is two. With the engineer's model and the registration offset (model-yardstick's
"column registration (dx, dy) mm", our frame -> hers), each near-miss is also held against her nearest column on that storey:

  OFF HER STACK  our column misses hers by about what the stack misses (within 150 mm of it), and the stack does not:
                 the storeys above and below agree with her and this column does not
  PARTNER        our column is on hers (within 150 mm): the column on the next storey is the one that moved
  NOT JUDGED     her model has no column within 1,000 mm (a part of the set she did not model there)

Exit 1 when any OFF HER STACK is found (0 otherwise; 2 on bad arguments).

⚠ OFF HER STACK SAYS WHERE THE COLUMN IS, NOT WHY. It was called WRONG TWIN until 2026-10-27, and that named a cause it does not
test: the build keeping the wrong one of two readings of one drawing. Traced through each sheet's placement (twin_trace.py,
twin_look.py, 350f8761), 7 of 7 on 31065 L2 and 31170 L7 were not that - each was DRAWN there by the lowest sheet of a run (a
column turned 90 degrees, shifted, or changed in size at that floor) while the engineer's model keeps it straight. Which one a
line is, a twin chosen wrongly or a drawn transition, is decided by twin_trace.py on a gate work dir, not by this.

WHY (2026-10-26, rule 11's trigger: three instances at two commits). When a column is drawn twice a few hundred mm apart,
the build keeps one - right, she models one - but nothing decides WHICH: 8ed10d03 kept the twin further from hers on
31138 L2 three times in twelve (0 -> 342, 2 -> 255, 2 -> 498 mm) and on 31065 L2 once (89 -> 402), and 674b2890 swapped one
on 31138 L2 off hers (30 -> 634). The rule that should hold: the column kept is the one the grid and the storeys above and
below agree on. This finds the columns that break it in a finished model, without a bank to diff against.

Measured on the six sets (develop at 506be566, 2026-10-26): 45 near-miss columns of 4,164 (31202: none) - 11 OFF HER STACK
(then labelled WRONG TWIN), 11 PARTNER, 22 NOT JUDGED, 1 OFF HERS. Of the 11, 7 are traced (2026-10-27, 350f8761) and all 7
are drawn transitions (31065 L2 x3, 31170 L7 x4); 31138 L2 x3 and 31130 L14 x1 are not yet traced. OFF HER STACK: 31138 L2 x3 (three of the four known instances; the fourth, the twin kept
at (41,896, 43,406), is a near-miss reading OFF HERS - its stack misses by 646 mm, hers by 342), 31065 L2 x3, 31130 L14 x1,
31170-arch L7 x4. The 8 beyond the known instances are NOT yet looked at on their drawings.

COVERS: column plan positions per storey, against the storeys directly above and below; the engineer's columns when given.
DOES NOT: a column with no column above or below it (a transfer, a column that starts or stops there) - so it MISSES the
known 31065 L2 instance at (19,433, 25,051) (the twin kept 402 mm off hers, nothing above or below there to disagree); a
wrong twin whose stack partner is itself off hers by a different amount (31138's fourth, OFF HERS above); offsets over 700 mm (a stack
that truly moves, or a different column); walls; sections; which of two near-miss storeys is right where neither has her.
A same-class fault it would not catch: both twins dropped and a third reading kept, consistently wrong on every storey.
"""
import math, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import model, members, norm   # noqa: E402


def columns_by_storey(path):
    _, pts, cols, _, _, _, lines = model(path)
    order = [m.group(1) for s in lines if (m := re.match(r'\s*STORY\s+"([^"]+)"', s))]
    at = {}
    for s in lines:
        if (m := re.match(r'\s*LINEASSIGN\s+"([^"]+)"\s+"([^"]+)"', s)) and m.group(1) in cols and (p := pts.get(cols[m.group(1)][0])):
            at.setdefault(m.group(2), []).append(p)
    return order, at


def near_misses(order, at):
    found = []
    for i, st in enumerate(order):
        nb = [order[j] for j in (i - 1, i + 1) if 0 <= j < len(order) and at.get(order[j])]
        for p in at.get(st, []):
            ds = [min(math.hypot(p[0] - q[0], p[1] - q[1]) for q in at[n]) for n in nb]
            if ds and min(ds) > 50 and any(100 <= d <= 700 for d in ds):
                found.append((st, p, min(ds)))
    return found


if __name__ == "__main__":
    a = sys.argv[1:]
    if len(a) not in (1, 3):
        print(__doc__.split("\n\n")[0]); sys.exit(2)
    order, at = columns_by_storey(a[0])
    found = near_misses(order, at)
    hers = members(a[1]) if len(a) == 3 else None
    dx, dy = map(float, a[2].split(",")) if len(a) == 3 else (0.0, 0.0)
    wrong = 0
    print(f"{len(found)} near-miss column(s) of {sum(len(v) for v in at.values())}")
    for st, p, d in found:
        verdict = ""
        if hers is not None:
            hs = next((n for n in [norm(st)] + [norm(x) for x in st.split("/")] if n in hers), None)
            dh = min((math.hypot(p[0] + dx - q[0], p[1] + dy - q[1]) for q in (hers[hs]["column"] if hs else [])), default=float("inf"))
            verdict = ("NOT JUDGED" if dh > 1000 else "PARTNER" if dh <= 150 else "OFF HER STACK" if abs(dh - d) <= 150 else "OFF HERS") + f" (hers {dh:,.0f} mm)"
            wrong += verdict.startswith("OFF HER STACK")
        print(f"  {st:<8} ({p[0]:.0f} {p[1]:.0f}) stack misses by {d:.0f} mm  {verdict}")
    if hers is not None: print(f"OFF HER STACK: {wrong} (a twin chosen wrongly OR a drawn transition - twin_trace.py decides)")
    sys.exit(1 if wrong else 0)
