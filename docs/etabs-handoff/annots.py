"""Every annotation in a markup PDF: page, type, author, text, colour, position."""
import sys

import fitz

doc = fitz.open(sys.argv[1])
total = 0
for pno in range(doc.page_count):
    page = doc[pno]
    items = list(page.annots() or [])
    if not items:
        continue
    print(f"--- page {pno + 1}  ({len(items)} annotation(s)) " + "-" * 40)
    for a in items:
        info = a.info
        colour = a.colors.get("stroke") or a.colors.get("fill")
        rgb = ""
        if colour:
            rgb = " rgb(" + ",".join(f"{int(c * 255)}" for c in colour) + ")"
        r = a.rect
        text = (info.get("content") or "").strip().replace("\n", " ")
        subject = (info.get("subject") or "").strip()
        title = (info.get("title") or "").strip()
        print(f"  [{a.type[1]:<12}]{rgb} by {title!r} subj={subject!r}")
        print(f"      rect ({r.x0:.0f},{r.y0:.0f})-({r.x1:.0f},{r.y1:.0f})")
        if text:
            print(f"      TEXT: {text}")
        total += 1

print()
print(f"{total} annotation(s) across {doc.page_count} page(s)")
