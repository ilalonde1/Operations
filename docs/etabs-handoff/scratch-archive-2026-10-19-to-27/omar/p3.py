"""Page 3 (A102, levels 1 and 2): the big markups (polygons over 50 pt), their colour, box, text; render the left view."""
import fitz
doc = fitz.open(r"C:\Users\ilalonde\Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
p = doc[2]
for a in p.annots():
    r = a.rect
    if a.type[1] in ("Polygon", "Square", "PolyLine") and max(r.width, r.height) > 50:
        f = a.colors.get("fill"); s = a.colors.get("stroke")
        print(a.type[1], [round(v) for v in r], "fill", [round(c, 2) for c in f] if f else None, "stroke", [round(c, 2) for c in s] if s else None,
              repr(a.info.get("content", "")[:20]), a.info.get("subject", ""))
p.get_pixmap(clip=fitz.Rect(150, 300, 1000, 1700), dpi=50).save(r"C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\omar\p3-left.png")
