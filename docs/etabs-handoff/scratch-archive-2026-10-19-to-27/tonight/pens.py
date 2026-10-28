r"""pens.py <pdf> <page> <x0,y0,x1,y1> : every stroked path in that box by pen (width, colour, dashes), with its item kinds
(l = line, c = curve, re = rect) and total length - which pen draws which boundary."""
import sys, collections, math
import fitz
pdf, pg, box = sys.argv[1], int(sys.argv[2]), fitz.Rect(*map(float, sys.argv[3].split(",")))
page = fitz.open(pdf)[pg - 1]
agg = collections.defaultdict(lambda: [0, 0.0, collections.Counter()])
for d in page.get_drawings():
    if d.get("type") not in ("s", "fs") or not d["rect"].intersects(box): continue
    key = (round(d.get("width") or 0, 2), tuple(round(c, 2) for c in (d.get("color") or ())), d.get("dashes") or "")
    for it in d["items"]:
        k = it[0]
        if k == "l": L = math.dist(it[1], it[2])
        elif k == "c": L = math.dist(it[1], it[4])
        elif k == "re": L = 2 * (it[1].width + it[1].height)
        else: L = 0
        a = agg[key]; a[0] += 1; a[1] += L; a[2][k] += 1
for key, (n, L, kinds) in sorted(agg.items(), key=lambda kv: -kv[1][1]):
    print(f"width {key[0]:<5} colour {key[1]} dashes {key[2]!r:<14} items {n:>5}  length {L:>8.0f} pt  {dict(kinds)}")
