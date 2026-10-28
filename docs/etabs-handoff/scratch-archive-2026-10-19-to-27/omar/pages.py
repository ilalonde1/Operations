"""Per page: how many markups are in KOR's structure ink (#800000 fill or stroke), and the page's view titles."""
import fitz
doc = fitz.open(r"C:\Users\ilalonde\Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
for i, p in enumerate(doc):
    ink = total = 0
    for a in p.annots():
        if a.type[1] in ("Link", "Popup"): continue
        total += 1
        c = a.colors.get("fill") or a.colors.get("stroke") or []
        if len(c) == 3 and abs(c[0] - 0.502) < 0.02 and c[1] < 0.02 and c[2] < 0.02: ink += 1
    print(f"page {i + 1}: {total} markups, {ink} in structure ink")
