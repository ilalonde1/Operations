"""How far a model's columns sit from the yardstick's, storey by storey — the positional check the counts never give.

    python docs/etabs-handoff/columns_vs_yardstick.py <model.e2k> <model unit: mm|in> <yardstick.e2k> <yardstick unit> [storey]

The two frames are matched BY GRID NAME: the same axis label in both files is the same line, so the offset between
the models is the median difference of the shared X labels and of the shared Y labels (the PDF-only model carries
GRIDS since step 39; before that it had none and this could only guess from the columns themselves). Then every
column in the model is matched to its nearest column in the yardstick on the storeys both name (prefixes the two
routes use are stripped: A-L5 / L5), and the residuals are summarised: median, and the share within 50, 100 and
300 mm. Built 2026-09-10 to measure the centroid fix (every column read by shape had sat off centre towards its
last-drawn side) against the Revit route's 31168 model, the one yardstick in the harness that is not our own output.
"""
import os
import re
import statistics
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(__file__))
from plan_sheet import read

UNIT = {"mm": 1.0, "in": 25.4}
GRID = re.compile(r'\s*GRID\s+"([^"]*)"\s+LABEL\s+"([^"]+)"\s+DIR\s+"([XY])"\s+COORD\s+(-?[\d.]+)')


def columns(path, unit):
    order, area, line, kind, on = read(path)
    k = UNIT[unit]
    out = {}
    for storey in order:
        pts = []
        for n in on.get(storey, []):
            if n in line and kind.get(n) == 'COLUMN':
                (x0, y0), _ = line[n]
                pts.append((x0 * k, y0 * k))
        if pts: out[storey] = pts
    return out


def grids(path, unit):
    k = UNIT[unit]
    out = {}
    for raw in open(path, encoding='utf-8', errors='replace'):
        m = GRID.match(raw)
        if m: out[(m.group(1), m.group(2).upper(), m.group(3))] = float(m.group(4)) * k   # keyed by grid SYSTEM too (Codex audit 2026-09-11, F23)
    return out


def key(storey):
    s = storey.upper()
    s = re.sub(r"^[A-C]-", "", s)
    return s.replace("LEVEL ", "L").replace("LEVEL", "L")


def main():
    model, mu, yard, yu = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
    only = sys.argv[5] if len(sys.argv) > 5 else None
    m = columns(model, mu); y = columns(yard, yu)
    # a storey is matched by its FULL name first (A-L1 to A-L1), and by its stripped name only when the
    # yardstick has no full-name match - A-L1 and B-L1 both stripped to L1 and one overwrote the other
    # (Codex audit 2026-09-11, F23)
    yfull = {s_.upper(): p for s_, p in y.items()}
    ystripped = {}
    for s_, p in y.items(): ystripped.setdefault(key(s_), []).extend(p)
    def yard_for(storey):
        return yfull.get(storey.upper()) or ystripped.get(key(storey))
    gm, gy = grids(model, mu), grids(yard, yu)
    shared = [k for k in gm if k in gy]
    dx = [gy[k] - gm[k] for k in shared if k[2] == 'X']; dy = [gy[k] - gm[k] for k in shared if k[2] == 'Y']
    if len(dx) >= 2 and len(dy) >= 2:
        shift = (statistics.median(dx), statistics.median(dy))
        spread = max(max(dx) - min(dx), max(dy) - min(dy))
        print(f"frames matched on {len(dx)} X and {len(dy)} Y grid labels both models name; offset ({shift[0]:,.0f}, {shift[1]:,.0f}) mm, the labels' own disagreement up to {spread:,.0f} mm")
    else:
        shift = None
        print(f"only {len(dx)} X / {len(dy)} Y grid labels shared - falling back to the modal displacement, which is a guess")
    votes = Counter()
    pairs = []
    for s, pts in m.items():
        if only and s != only: continue
        ypts = yard_for(s)
        if not ypts: continue
        for p in pts:
            pp = (p[0] + shift[0], p[1] + shift[1]) if shift else p
            q = min(ypts, key=lambda q: (q[0] - pp[0]) ** 2 + (q[1] - pp[1]) ** 2)
            votes[(round((q[0] - p[0]) / 50) * 50, round((q[1] - p[1]) / 50) * 50)] += 1
            pairs.append((s, p, q))
    if not pairs:
        print("no storey both models name"); return
    if shift is None: shift = votes.most_common(1)[0][0]
    res = [((q[0] - p[0] - shift[0]) ** 2 + (q[1] - p[1] - shift[1]) ** 2) ** 0.5 for _, p, q in pairs]
    n = len(res)
    print(f"{n} columns on shared storeys; residual to the nearest yardstick column: median {statistics.median(res):,.0f} mm; "
          f"within 50 mm {sum(r <= 50 for r in res)} ({100 * sum(r <= 50 for r in res) / n:.0f}%), within 100 mm {sum(r <= 100 for r in res)} ({100 * sum(r <= 100 for r in res) / n:.0f}%), "
          f"within 300 mm {sum(r <= 300 for r in res)} ({100 * sum(r <= 300 for r in res) / n:.0f}%)")
    by = {}
    for (s, _, _), r in zip(pairs, res): by.setdefault(s, []).append(r)
    # EVERY storey, largest first - the first version printed the eight largest and a "tower storeys 96% within
    # 100 mm" was written from that sample as if it were the population (PdfIntake.md §48, withdrawn 2026-09-11)
    print(f"{len(by)} storeys both models name, every one:")
    for s in sorted(by, key=lambda s: -len(by[s])):
        rs = by[s]
        print(f"   {s:10} {len(rs):4} columns  median {statistics.median(rs):,.0f} mm  within 100 mm {100 * sum(r <= 100 for r in rs) / len(rs):.0f}%")


main()
