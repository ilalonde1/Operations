r"""storey_plate_overlap.py <model.e2k|walk-dir> [...] : floor plates of one storey that lie over one another.

For each pair of FLOOR plates (over 50 sq ft) on one storey that share more than 1 sq ft of ground: the smaller's area, the
share of its ground inside the larger, and how far it reaches past. Given a directory, every <job>\out.e2k under it. Exit 1
when any pair overlaps, 0 when none, 2 on bad arguments.

WHY (2026-10-26). A plate over another is concrete written twice. The six-set drift work met it first as 31170-arch L5, where
the continuation fix (parked, wip/intake-170-continuation) let an architectural part plan's floor reach past the overall plan
and escape the part-plan absorption (DxfToEtabsService, steps 103/176). Measured, it is not that fix's alone: on develop the
same set's L1 and L6 already carry part-plan plates 86-87% inside the 32,076 sq ft floor, and the 24-set replay (wood/arm-on4)
holds 67 overlapping pairs, spread over every band from 0-9% inside (19) to wholly inside (6) - north and south halves drawn
past their match line (7-21%), a whole floor read twice from two sheets (31006 L1: 21,425 over 22,590 sq ft, 87%), part plans.
The pipeline reports each as "stands beyond the storey's floor ... left as read for the engineer" - a decided policy. So the
class is one sentence - A STOREY'S PLATES OVERLAP - and no share threshold separates its kinds: the fix is a decision first.

COVERS: FLOOR area plates by storey, by geometry (shapely), any thickness.
DOES NOT: which sheet each plate came from (the e2k does not say; the build's report.txt does); openings cut in either; whether
an overlap is meant (a mezzanine, a pit lid the engineer models twice); walls.
SAME-CLASS FAULT IT WOULD NOT CATCH: two readings of one floor that touch edge to edge without overlapping (a floor split in
two along a match line) - written once each, no overlap, still two plates.
"""
import os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from judge_drift_moves import model   # noqa: E402
from shapely.geometry import Polygon

SQFT = 92903.04


def overlaps(path):
    _, pts, _, _, floors, _, lines = model(path)
    by = {}
    for s in lines:
        if (m := re.match(r'\s*AREAASSIGN\s+"([^"]+)"\s+"([^"]+)"\s+SECTION', s)) and m.group(1) in floors:
            p = [pts[q] for q in floors[m.group(1)] if q in pts]
            if len(p) >= 3 and (g := Polygon(p).buffer(0)).area > 50 * SQFT:
                by.setdefault(m.group(2), []).append((m.group(1), g))
    for st, ps in by.items():
        ps.sort(key=lambda x: -x[1].area)
        for i, (na, a) in enumerate(ps):
            for nb, b in ps[i + 1:]:
                inter = a.intersection(b).area
                if inter > SQFT:
                    yield st, nb, b.area / SQFT, na, a.area / SQFT, inter / b.area, (b.area - inter) / SQFT


if __name__ == "__main__":
    if not sys.argv[1:]:
        print(__doc__.split("\n\n")[0]); sys.exit(2)
    files = []
    for arg in sys.argv[1:]:
        if os.path.isdir(arg):
            files += [os.path.join(arg, d, "out.e2k") for d in sorted(os.listdir(arg)) if os.path.exists(os.path.join(arg, d, "out.e2k"))]
        else:
            files.append(arg)
    n = 0
    for f in files:
        for st, nb, ba, na, aa, share, past in overlaps(f):
            n += 1
            print(f"{os.path.basename(os.path.dirname(f))} {st}: {nb} {ba:,.0f} sq ft over {na} {aa:,.0f}: {share:.0%} inside, {past:,.0f} sq ft past")
    print(f"{n} overlapping pair(s) in {len(files)} model(s)")
    sys.exit(1 if n else 0)
