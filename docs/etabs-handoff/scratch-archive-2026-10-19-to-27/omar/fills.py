"""Page 6 markups: stroke, fill (interior colour) and opacity of each, by kind."""
import fitz, collections
doc = fitz.open(r"C:\Users\ilalonde\Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
p = doc[5]
rows = collections.Counter()
for a in p.annots():
    if a.type[1] not in ("Polygon", "Square", "Line", "PolyLine"): continue
    content = a.info.get("content", "")
    kind = "4,436 floor" if content == "4,436 sf" else ("504 column" if content == "504 sq in" else ("wall region" if a.type[1] == "Polygon" else a.type[1]))
    fill = a.colors.get("fill")
    rows[(kind, str(a.colors.get("stroke")), str(fill), round(a.opacity, 2))] += 1
for k, n in sorted(rows.items()): print(n, k)
