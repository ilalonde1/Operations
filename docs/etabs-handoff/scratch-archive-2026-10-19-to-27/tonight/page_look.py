r"""page_look.py <pdf> <page 1-based> <outprefix> <regex> [dpi]: the whole page small, and a crop round each word run matching
the regex (e.g. a thickness call-out) - to LOOK at what the drawing says where a zone is."""
import re, sys
import fitz
pdf, pg, out, rx = sys.argv[1], int(sys.argv[2]), sys.argv[3], re.compile(sys.argv[4])
dpi = int(sys.argv[5]) if len(sys.argv) > 5 else 110
doc = fitz.open(pdf); page = doc[pg - 1]
page.get_pixmap(dpi=24).save(f"{out}-page.png")
words = page.get_text("words")
lines = {}
for w in words: lines.setdefault((w[5], w[6]), []).append(w)
hits = []
for ws in lines.values():
    t = " ".join(w[4] for w in ws)
    if rx.search(t):
        r = fitz.Rect(min(w[0] for w in ws), min(w[1] for w in ws), max(w[2] for w in ws), max(w[3] for w in ws))
        hits.append((t, r))
print(f"page {pg}: {page.rect.width:.0f} x {page.rect.height:.0f} pt, {len(hits)} call-outs matching")
for t, r in hits: print(f"   {t!r} at ({r.x0:.0f}, {r.y0:.0f})")
if hits:
    u = fitz.Rect(hits[0][1])
    for _, r in hits[1:]: u |= r
    u = fitz.Rect(u.x0 - 250, u.y0 - 250, u.x1 + 250, u.y1 + 250) & page.rect
    page.get_pixmap(dpi=dpi, clip=u).save(f"{out}-callouts.png"); print(f"{out}-callouts.png  clip {u}")
