import sys, csv
from PIL import Image, ImageDraw
# leak.py <dump> x0 y0 x1 y1 ix iy : thicken the arrangement's lines until the inside point is sealed from the outside, and
# print where the seal was made (model mm) - the leaks, each a gap about as wide as the thickness that sealed it
f = sys.argv[1]; x0, y0, x1, y1, ix, iy = map(float, sys.argv[2:8])
mm_px = 20.0; s = 1 / mm_px
W, H = int((x1 - x0) * s), int((y1 - y0) * s)
rows = [tuple(map(float, r[1:5])) for r in csv.reader(open(f))]
P = lambda x, y: ((x - x0) * s, (y1 - y) * s)
def draw(w):
    im = Image.new('L', (W, H), 255); d = ImageDraw.Draw(im)
    for a, b, c, e in rows: d.line([P(a, b), P(c, e)], fill=0, width=w)
    return im
def flood(im, seed, val):
    im = im.copy(); ImageDraw.floodfill(im, seed, val); return im
ip = tuple(int(v) for v in P(ix, iy))
prev = None
for w in range(1, 40):
    im = draw(w); o = flood(im, (0, 0), 100)
    if o.getpixel(ip) != 100:
        inside = flood(o, ip, 200)
        pi, po, pp = inside.load(), o.load(), prev.load()
        spots = []
        for y in range(1, H - 1):
            for x in range(1, W - 1):
                if pi[x, y] != 0 or pp[x, y] == 0: continue     # newly blocked at this width only
                nb = [pi[x + dx, y + dy] for dx in (-1, 0, 1) for dy in (-1, 0, 1)]
                if 200 in nb and 100 in nb: spots.append((x, y))
        print(f"sealed at line width {w} px = {w * mm_px:.0f} mm; {len(spots)} seal pixel(s)")
        seen = []
        for x, y in spots:
            X, Y = x * mm_px + x0, y1 - y * mm_px
            if all(abs(X - a) > 500 or abs(Y - b) > 500 for a, b in seen):
                seen.append((X, Y)); print(f"  leak near ({X:.0f}, {Y:.0f}) mm")
        break
    prev = im
