"""GROUND TRUTH for "is this a hatch?": the page's own closed rectangles, binned by size, and whether each
sits under a clip smaller than itself (a pattern tile showing only through its clip) — through fitz,
with no reader in the way.

    python docs/etabs-handoff/pdf_tiles.py <pdf> <page1based> [minPt] [maxPt]

Prints, for closed 4-point paths whose box is between minPt and maxPt on paper (default 8–60 pt):
size bins with counts, how many are filled/stroked, how many lie under a clip that does not contain
them (clipped: only part of the tile shows), and the clip rects those tiles share. A hatch is a
pattern of identical cells on a fixed pitch, showing only through the clip that is the region's
outline; a column is a whole shape that nothing clips.

Built 2026-09-10 when 31170's LEVEL P1 PLAN read 311 "columns" of which 248 were 900 x 1,200 mm
boxes standing shoulder to shoulder along every wall — the hatch of the hatched walls.
"""
import collections
import sys

import fitz


def main():
    pdf, page_no = sys.argv[1], int(sys.argv[2])
    lo = float(sys.argv[3]) if len(sys.argv) > 3 else 8.0
    hi = float(sys.argv[4]) if len(sys.argv) > 4 else 60.0
    page = fitz.open(pdf)[page_no - 1]
    drawings = page.get_drawings(extended=True)
    # clip stack by level: an entry of type 'clip' applies to later entries with a deeper level
    clips = {}
    bins = collections.Counter()
    inked = collections.Counter()
    clipped = collections.Counter()
    clip_rects = collections.Counter()
    total = 0
    for d in drawings:
        t = d.get("type")
        lvl = d.get("level", 0)
        if t == "clip":
            clips[lvl] = d.get("scissor")
            for k in [k for k in clips if k > lvl]:
                del clips[k]
            continue
        items = d.get("items") or []
        r = d.get("rect")
        if r is None: continue
        w, h = r.width, r.height
        if not (lo <= max(w, h) <= hi and min(w, h) >= 2): continue
        # closed 4-point path: a rectangle (an 're' item, or four line items closing)
        is_rect = any(it[0] == "re" for it in items) or (len(items) >= 3 and all(it[0] == "l" for it in items) and d.get("closePath"))
        if not is_rect: continue
        total += 1
        key = (round(min(w, h)), round(max(w, h)))
        bins[key] += 1
        if d.get("fill") is not None: inked[(key, "filled")] += 1
        if d.get("color") is not None: inked[(key, "stroked")] += 1
        scissor = None
        for k in sorted(clips):
            if k < lvl: scissor = clips[k]
        if scissor is not None and not scissor.contains(r):
            clipped[key] += 1
            clip_rects[(round(scissor.x0), round(scissor.y0), round(scissor.x1), round(scissor.y1))] += 1
    print(f"page {page_no}: {total} closed rectangles between {lo:g} and {hi:g} pt on paper")
    for key, n in bins.most_common(12):
        print(f"  {key[0]:3} x {key[1]:3} pt : {n:4}   filled {inked[(key, 'filled')]:4}  stroked {inked[(key, 'stroked')]:4}  under a clip smaller than themselves {clipped[key]:4}")
    if clip_rects:
        print(f"  clips those tiles show through: {len(clip_rects)} distinct; the busiest:")
        for rc, n in clip_rects.most_common(6):
            print(f"    clip x {rc[0]}..{rc[2]}  y {rc[1]}..{rc[3]}  ({rc[2]-rc[0]} x {rc[3]-rc[1]} pt): {n} tiles")


main()
