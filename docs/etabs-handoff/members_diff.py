"""Which members a second .e2k lost or gained against a first, per storey, with positions — the "what exactly
changed" behind storey_counts.py's totals.

    python docs/etabs-handoff/members_diff.py <before.e2k> <after.e2k> [storey]

Columns are matched by their base joint (x, y) rounded to the unit; walls by the centroid of their panel. Prints,
per storey, the columns and walls only in the first file (LOST) and only in the second (GAINED), so a rule that
moved a count can be followed to the spot on the drawing and LOOKED at (CLAUDE.md rule 9) instead of argued over.

Built 2026-09-10 when step 37 (a pattern's cells are not columns) cost 31138 L1 five columns and the question
was whether those five were cells or an L-shaped column drawn as two rectangles.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from plan_sheet import read


def members(path):
    order, area, line, kind, on = read(path)
    out = {}
    for storey in order:
        cols, walls = set(), set()
        for n in on.get(storey, []):
            if n in line and kind.get(n) == 'COLUMN':
                (x0, y0), _ = line[n]
                cols.add((round(x0), round(y0)))
            elif n in area and kind.get(n) == 'PANEL':
                ps = area[n]
                walls.add((round(sum(p[0] for p in ps) / len(ps)), round(sum(p[1] for p in ps) / len(ps))))
        out[storey] = (cols, walls)
    return order, out


def main():
    a, b = sys.argv[1], sys.argv[2]
    only = sys.argv[3] if len(sys.argv) > 3 else None
    order, ma = members(a)
    _, mb = members(b)
    # the whole model can shift when its origin re-derives (a new wall at the edge moves the extent):
    # the modal displacement between each column in A and its nearest column in B is that shift, and
    # is taken out before matching, so a shifted model reads as unchanged and a moved member as moved
    from collections import Counter
    votes = Counter()
    for storey in order:
        ca, _ = ma.get(storey, (set(), set())); cb, _ = mb.get(storey, (set(), set()))
        for p in ca:
            if not cb: continue
            q = min(cb, key=lambda q: (q[0] - p[0]) ** 2 + (q[1] - p[1]) ** 2)
            votes[(q[0] - p[0], q[1] - p[1])] += 1
    shift = votes.most_common(1)[0][0] if votes else (0, 0)
    if shift != (0, 0):
        print(f"(the second model sits {shift[0]:+,}, {shift[1]:+,} from the first; positions below are the first model's frame)")
        mb = {k: ({(x - shift[0], y - shift[1]) for x, y in c}, {(x - shift[0], y - shift[1]) for x, y in w}) for k, (c, w) in mb.items()}
    for storey in order:
        if only and storey != only: continue
        ca, wa = ma.get(storey, (set(), set()))
        cb, wb = mb.get(storey, (set(), set()))
        # a joint can move a unit when the model's offset re-rounds; a member within two units is the same member
        def unmatched(xs, ys):
            return sorted(p for p in xs if not any(abs(p[0] - q[0]) <= 2 and abs(p[1] - q[1]) <= 2 for q in ys))
        lost_c, gained_c = unmatched(ca, cb), unmatched(cb, ca)
        lost_w, gained_w = unmatched(wa, wb), unmatched(wb, wa)
        if not (lost_c or gained_c or lost_w or gained_w): continue
        print(f"{storey}: columns {len(ca)} -> {len(cb)}, walls {len(wa)} -> {len(wb)}")
        for tag, items in (("LOST column", lost_c), ("GAINED column", gained_c), ("LOST wall", lost_w), ("GAINED wall", gained_w)):
            for x, y in items[:12]:
                print(f"   {tag:14} at ({x:,}, {y:,})")
            if len(items) > 12: print(f"   ... and {len(items) - 12} more")


main()
