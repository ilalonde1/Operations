import fitz, collections
doc = fitz.open(r"C:\Users\ilalonde\Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
p = doc[5]
print("page", p.rect, "rot", p.rotation)
c = collections.Counter()
for a in p.annots():
    c[a.type[1]] += 1
    print(a.type[1], [round(v) for v in a.rect], repr(a.info.get("content", "")[:30]),
          a.colors.get("stroke"), "subj=" + a.info.get("subject", ""))
print(c, sum(c.values()))
r = p.rect
pix = p.get_pixmap(clip=fitz.Rect(r.width * 0.53, r.height * 0.17, r.width * 0.80, r.height * 0.88), dpi=110)
pix.save(r"C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\omar\p6-plan.png")
