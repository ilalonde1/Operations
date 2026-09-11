"""Which members of an .e2k stand beyond every floor plate of their storey — and how many that is of the storey.

    python docs/etabs-handoff/outside_plate.py out.e2k [reach_units] [storey]

Per storey: walls and columns whose midpoint/centre lies outside every FLOOR area on that storey by more than
`reach` model units (default 150 mm / 6 in — pass 6 for an inch model), as "N of M", then each member's
position. A storey with no plate is skipped. Uses plan_sheet.read, so it sees exactly what the render sees.

Built 2026-09-10 to answer "which two walls stand above L2's plate on 31170" without guessing, and kept because
the same question comes up on every set: it is the per-storey measure of whether the floor was read whole.
"""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from plan_sheet import read


def inside(p, poly):
    x, y = p; n = len(poly); ins = False
    for i in range(n):
        (x0, y0), (x1, y1) = poly[i], poly[(i + 1) % n]
        if (y0 > y) != (y1 > y) and x < (x1 - x0) * (y - y0) / (y1 - y0 + 1e-12) + x0:
            ins = not ins
    return ins


def dist_seg(p, a, b):
    (px, py), (ax, ay), (bx, by) = p, a, b
    dx, dy = bx - ax, by - ay
    if dx == dy == 0: return ((px - ax) ** 2 + (py - ay) ** 2) ** 0.5
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return ((px - ax - t * dx) ** 2 + (py - ay - t * dy) ** 2) ** 0.5


def near(p, poly, reach):
    return inside(p, poly) or any(dist_seg(p, poly[i], poly[(i + 1) % len(poly)]) <= reach for i in range(len(poly)))


def main():
    path = sys.argv[1]
    reach = float(sys.argv[2]) if len(sys.argv) > 2 else 150.0
    only = sys.argv[3] if len(sys.argv) > 3 else None
    order, area, line, kind, on = read(path)
    for storey in order:
        if only and storey != only: continue
        plates = [area[a] for a in on.get(storey, []) if a in area and kind.get(a) == 'FLOOR' and len(area[a]) >= 3]
        if not plates: continue
        members = []
        for n in on.get(storey, []):
            if n in line:
                (x0, y0), (x1, y1) = line[n]
                members.append((n, kind.get(n, '?'), ((x0 + x1) / 2, (y0 + y1) / 2)))
            elif n in area and kind.get(n) == 'PANEL':
                ps = area[n]
                members.append((n, 'WALL', (sum(p[0] for p in ps) / len(ps), sum(p[1] for p in ps) / len(ps))))
        out = [(n, k, p) for n, k, p in members if not any(near(p, pl, reach) for pl in plates)]
        if not out: continue
        by = {}
        for n, k, p in out: by.setdefault(k, []).append((n, p))
        tot = {}
        for n, k, p in members: tot[k] = tot.get(k, 0) + 1
        parts = ', '.join(f"{len(v)} of {tot.get(k, 0)} {k.lower()}(s)" for k, v in sorted(by.items()))
        print(f"{storey}: {parts} beyond every plate ({len(plates)} plate(s))")
        for k, v in sorted(by.items()):
            for n, (x, y) in v[:8]:
                print(f"   {k:6} {n:8} at ({x:,.0f}, {y:,.0f})")
            if len(v) > 8: print(f"   ... and {len(v) - 8} more {k.lower()}(s)")


main()
