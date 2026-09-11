"""Which members a second .e2k lost or gained against a first, per storey, with positions — the "what exactly
changed" behind storey_counts.py's totals.

    python docs/etabs-handoff/members_diff.py <before.e2k> <after.e2k> [storey]

Columns are matched by their base joint (x, y); walls by the centroid of their panel AND its length, so a wall
turned through ninety degrees about its centre is a change. Storeys are the UNION of both files' storeys, so a
storey only the second file has is reported. The two models' frames are matched BY GRID LABEL when both carry
GRIDS (the same label in both files is the same line); when either has none, the modal displacement between
each column and its nearest neighbour is taken out instead, and the report says which was used — the modal
guess reads a lone member's move as a frame shift and a whole-model shift as member moves (Codex audit
2026-09-11, F22), so it is the fallback, not the method. Every storey line ends with the COUNTS lost and gained,
and six_set_diff.sh reads those counts, never the printed sample of positions (F24).

Built 2026-09-10 when step 37 cost 31138 L1 five columns and the question was whether those five were cells or
an L-shaped column drawn as two rectangles; corrected 2026-09-11 after the audit.
"""
import os
import re
import statistics
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(__file__))
from plan_sheet import read

GRID = re.compile(r'\s*GRID\s+"([^"]*)"\s+LABEL\s+"([^"]+)"\s+DIR\s+"([XY])"\s+COORD\s+(-?[\d.]+)')


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
                xs = [p[0] for p in ps]; ys = [p[1] for p in ps]
                length = round(max(max(xs) - min(xs), max(ys) - min(ys)))
                walls.add((round(sum(xs) / len(ps)), round(sum(ys) / len(ps)), length))
        out[storey] = (cols, walls)
    return order, out


def grids(path):
    out = {}
    for raw in open(path, encoding='utf-8', errors='replace'):
        m = GRID.match(raw)
        if m: out[(m.group(1), m.group(2).upper(), m.group(3))] = float(m.group(4))
    return out


def main():
    a, b = sys.argv[1], sys.argv[2]
    only = sys.argv[3] if len(sys.argv) > 3 else None
    order_a, ma = members(a)
    order_b, mb = members(b)
    order = list(order_a) + [s for s in order_b if s not in order_a]

    # the frame shift between the two models, by grid label first
    ga, gb = grids(a), grids(b)
    shared = [k for k in ga if k in gb]
    dx = [gb[k] - ga[k] for k in shared if k[2] == 'X']; dy = [gb[k] - ga[k] for k in shared if k[2] == 'Y']
    if len(dx) >= 2 and len(dy) >= 2:
        shift = (round(statistics.median(dx)), round(statistics.median(dy)))
        how = f"by {len(dx)} X and {len(dy)} Y grid labels both files carry"
    else:
        votes = Counter()
        for storey in order:
            ca, _ = ma.get(storey, (set(), set())); cb, _ = mb.get(storey, (set(), set()))
            for p in ca:
                if not cb: continue
                q = min(cb, key=lambda q: (q[0] - p[0]) ** 2 + (q[1] - p[1]) ** 2)
                votes[(q[0] - p[0], q[1] - p[1])] += 1
        shift = votes.most_common(1)[0][0] if votes else (0, 0)
        how = "by the modal column displacement - a GUESS, one file has no GRIDS"
    if shift != (0, 0):
        print(f"(the second model sits {shift[0]:+,}, {shift[1]:+,} from the first, {how}; positions below are the first model's frame)")
        mb = {k: ({(x - shift[0], y - shift[1]) for x, y in c}, {(x - shift[0], y - shift[1], l) for x, y, l in w}) for k, (c, w) in mb.items()}

    def unmatched(xs, ys):
        # a joint can move a unit when the model's offset re-rounds; a member within two units is the same member
        return sorted(p for p in xs if not any(all(abs(p[i] - q[i]) <= 2 for i in range(len(p))) for q in ys))

    for storey in order:
        if only and storey != only: continue
        ca, wa = ma.get(storey, (set(), set()))
        cb, wb = mb.get(storey, (set(), set()))
        lost_c, gained_c = unmatched(ca, cb), unmatched(cb, ca)
        lost_w, gained_w = unmatched(wa, wb), unmatched(wb, wa)
        if not (lost_c or gained_c or lost_w or gained_w): continue
        print(f"{storey}: columns {len(ca)} -> {len(cb)}, walls {len(wa)} -> {len(wb)}"
              f"  [lost columns {len(lost_c)}, gained columns {len(gained_c)}, lost walls {len(lost_w)}, gained walls {len(gained_w)}]")
        for tag, items in (("LOST column", lost_c), ("GAINED column", gained_c), ("LOST wall", lost_w), ("GAINED wall", gained_w)):
            for p in items[:12]:
                print(f"   {tag:14} at ({p[0]:,}, {p[1]:,})" + (f" {p[2]:,} long" if len(p) > 2 else ""))
            if len(items) > 12: print(f"   ... and {len(items) - 12} more (a sample is printed; the count is in the storey line)")


main()
