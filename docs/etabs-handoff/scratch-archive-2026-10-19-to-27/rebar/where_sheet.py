"""For each sheet number asked, list the pages of each PDF whose text names it, and whether it sits in the
title-block corner (bottom-right 25% x 20% of the page) - the sheet's own number - or elsewhere (a reference).
Args: before.pdf after.pdf SHEET [SHEET ...]"""
import sys, fitz

def scan(path, sheets):
    doc = fitz.open(path)
    out = {s: [] for s in sheets}
    for i, page in enumerate(doc):
        w, h = page.rect.width, page.rect.height
        for x0, y0, x1, y1, word, *_ in page.get_text("words"):
            for s in sheets:
                if word.strip(".,:;()") == s:
                    corner = x0 > w * 0.75 and y0 > h * 0.80
                    out[s].append((i + 1, "TITLE" if corner else "ref"))
    return out

before, after, sheets = sys.argv[1], sys.argv[2], sys.argv[3:]
for label, path in (("BEFORE", before), ("AFTER", after)):
    found = scan(path, sheets)
    for s in sheets:
        titles = sorted({p for p, k in found[s] if k == "TITLE"})
        refs = sorted({p for p, k in found[s] if k == "ref"})
        print(f"{label:6} {s:8} title-block on pages {titles}   referenced on {len(refs)} page(s)")
