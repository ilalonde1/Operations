import fitz
doc = fitz.open(r"C:/Users/ilalonde/Desktop/Omar/143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
p = doc[2]
for a in p.annots():
    r = a.rect
    if a.type[1] == "Polygon" and abs(r.x0 - 485) < 2 and abs(r.y0 - 383) < 2:
        v = a.vertices
        print(len(v), "vertices")
        for i, q in enumerate(v): print(i, round(q[0], 1), round(q[1], 1))
