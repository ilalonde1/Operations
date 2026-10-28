r"""plate_overlap.py <e2k> [storey...]: every pair of FLOOR plates on one storey that overlap - the smaller's area, the share of
its ground inside the larger, and how far it reaches past (sq ft). A plate over another is either a part plan's second reading
of the same floor (to absorb) or something the engineer models twice (a mezzanine, a pit lid) - this measures which shapes occur."""
import os, re, sys
sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools\CorpusGateBisect")
from judge_drift_moves import model   # noqa: E402
from shapely.geometry import Polygon

_, pts, _, _, floors, _, lines = model(sys.argv[1])
want = set(sys.argv[2:])
by = {}
for s in lines:
    if (m := re.match(r'\s*AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION', s)) and m.group(1) in floors:
        p = [pts[q] for q in floors[m.group(1)] if q in pts]
        if len(p) >= 3:
            g = Polygon(p).buffer(0)
            if g.area > 92903.04 * 50: by.setdefault(m.group(2), []).append((m.group(1), g))
for st, ps in by.items():
    if want and st not in want: continue
    ps.sort(key=lambda x: -x[1].area)
    for i, (na, a) in enumerate(ps):
        for nb, b in ps[i + 1:]:
            inter = a.intersection(b).area
            if inter <= 92903.04: continue
            print(f"{st}: {nb} {b.area / 92903.04:,.0f} sq ft over {na} {a.area / 92903.04:,.0f}: {inter / b.area:.0%} of its ground inside, "
                  f"{(b.area - inter) / 92903.04:,.0f} sq ft past it")
