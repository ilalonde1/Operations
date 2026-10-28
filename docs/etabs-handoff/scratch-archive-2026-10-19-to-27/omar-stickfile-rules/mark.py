"""Ring each listed model point on page 6 (model mm = page frame at 1:96) and render the plan area."""
import fitz
base = r"C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\omar-stickfile-rules"
doc = fitz.open(r"C:\Users\ilalonde\Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf")
p = doc[5]
k = 96 * 25.4 / 72
for line in open(base + r"\l35-out.txt"):
    name, x, y = line.split()
    px, py = float(x) / k, p.rect.height - float(y) / k
    p.draw_circle((px, py), 9, color=(0, 0.7, 0), width=2.5)
    p.insert_text((px + 10, py - 4), name, fontsize=9, color=(0, 0.5, 0))
r = p.rect
p.get_pixmap(clip=fitz.Rect(r.width * 0.50, r.height * 0.12, r.width * 0.84, r.height * 0.92), dpi=90).save(base + r"\l35-marked.png")
