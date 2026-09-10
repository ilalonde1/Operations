"""GROUND TRUTH: what the PDF itself draws in a region, with no reader in the way.

Reads the page's own paths through fitz and prints every long axis-aligned run, grouped by the
line they sit on. Use it to settle "is the drafter's line actually there?" before blaming a reader
-- on 2026-09-10 it showed 31168's tower slab edge IS drawn, 31,586 mm a side in three collinear
pieces, while the DXF held none of it, which is how the wall reader was caught eating it.

    python docs/etabs-handoff/pdf_lines.py <pdf> <page1based> <x0> <y0> <x1> <y1> <minPt>

Region is in PDF points, y DOWN. Lengths print in points and in millimetres at 1:96.
"""

import collections
import sys

import fitz

pdf, page = sys.argv[1], int(sys.argv[2])
x0, y0, x1, y1, minpt = (float(a) for a in sys.argv[3:8])
MM = 96 * 25.4 / 72.0

doc = fitz.open(pdf)
p = doc[page - 1]

hor = collections.defaultdict(list)
ver = collections.defaultdict(list)
n = 0
for d in p.get_drawings():
    for item in d["items"]:
        pts = []
        if item[0] == "l":
            pts = [item[1], item[2]]
        elif item[0] == "re":
            r = item[1]
            pts = None
            for a, b in ((r.tl, r.tr), (r.tr, r.br), (r.br, r.bl), (r.bl, r.tl)):
                if x0 <= a.x <= x1 and y0 <= a.y <= y1:
                    n += 1
                    if abs(a.y - b.y) < 0.3 and abs(a.x - b.x) >= minpt:
                        hor[round(a.y, 1)].append((min(a.x, b.x), max(a.x, b.x)))
                    if abs(a.x - b.x) < 0.3 and abs(a.y - b.y) >= minpt:
                        ver[round(a.x, 1)].append((min(a.y, b.y), max(a.y, b.y)))
            continue
        if not pts:
            continue
        a, b = pts
        if not (x0 <= a.x <= x1 and y0 <= a.y <= y1):
            continue
        n += 1
        if abs(a.y - b.y) < 0.3 and abs(a.x - b.x) >= minpt:
            hor[round(a.y, 1)].append((min(a.x, b.x), max(a.x, b.x)))
        if abs(a.x - b.x) < 0.3 and abs(a.y - b.y) >= minpt:
            ver[round(a.x, 1)].append((min(a.y, b.y), max(a.y, b.y)))

print(f"{n} segments in region; horizontals >= {minpt:.0f} pt ({minpt*MM:.0f} mm), by y (page y down):")
for y, spans in sorted(hor.items()):
    spans.sort()
    tot = sum(b - a for a, b in spans)
    print(f"  y={y:8.1f}  {len(spans):3d} run(s)  total {tot:7.1f} pt = {tot*MM:8.0f} mm   "
          f"x {spans[0][0]:.0f}..{spans[-1][1]:.0f}")
    print("        " + "  ".join(f"[{a:.0f},{b:.0f}]={(b-a)*MM:.0f}mm" for a, b in spans))
print()
print("verticals, by x:")
for x, spans in sorted(ver.items()):
    spans.sort()
    tot = sum(b - a for a, b in spans)
    print(f"  x={x:8.1f}  {len(spans):3d} run(s)  total {tot*MM:8.0f} mm   y {spans[0][0]:.0f}..{spans[-1][1]:.0f}")
