"""Page 8 (A107 roof): every structure-ink markup, its kind, box and text; and a render of the page with them."""
import fitz
doc = fitz.open(r"C:\Users\ilalonde\Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
p = doc[6]
print("page", p.rect)
for a in p.annots():
    c = a.colors.get("fill") or a.colors.get("stroke") or []
    ink = len(c) == 3 and abs(c[0] - 0.502) < 0.02 and c[1] < 0.02 and c[2] < 0.02
    print(("INK  " if ink else "     "), a.type[1], [round(v) for v in a.rect], repr(a.info.get("content", "")[:30]), a.info.get("subject", ""))
p.get_pixmap(dpi=40).save(r"C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\omar\p7.png")
