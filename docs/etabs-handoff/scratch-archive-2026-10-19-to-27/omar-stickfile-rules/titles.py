"""Lines of text naming a LEVEL/ROOF on a page, with any stroke just under them. usage: titles.py <pdf> <page index>"""
import fitz, sys, collections
doc = fitz.open(sys.argv[1]); p = doc[int(sys.argv[2])]
W = p.rect.width
lines = collections.defaultdict(list)
for x0, y0, x1, y1, w, b, l, n in p.get_text("words"):
    lines[(b, l)].append((x0, y0, x1, y1, w))
strokes = [d for d in p.get_drawings() if d["type"] in ("s", "fs") and d["rect"].height < 1.5 and d["rect"].width > 20]
for key, ws in lines.items():
    text = " ".join(w[4] for w in ws)
    if not any(t in text.upper() for t in ("LEVEL", "ROOF", "PLAN")): continue
    x0 = min(w[0] for w in ws); x1 = max(w[2] for w in ws); y1 = max(w[3] for w in ws); h = y1 - min(w[1] for w in ws)
    under = [d["rect"] for d in strokes if 0 <= d["rect"].y0 - y1 <= 2 * h and d["rect"].x0 < x1 and d["rect"].x1 > x0]
    u = " ".join(f"[{r.x0:.0f}-{r.x1:.0f} @{r.y0:.0f}]" for r in under)
    print(f"x {x0:6.0f}-{x1:6.0f} y {y1:5.0f} h {h:4.1f} {'TBLOCK ' if x0 > 0.8 * W else ''}'{text}'  under: {u or '-'}")
