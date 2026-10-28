import fitz, sys
from PIL import Image, ImageDraw
# flood.py <pdf> <page1> x0 y0 x1 y1 <inside_x> <inside_y> <out.png> [minpen] : draw the window's straight lines (pen >= minpen)
# at 4 px per paper mm, flood the outside from the window's corner, colour what the flood reached; the inside point
# is reached only if the linework leaks
pdf, page = sys.argv[1], int(sys.argv[2])
x0, y0, x1, y1, ix, iy = map(float, sys.argv[3:9])
out = sys.argv[9]; minpen = float(sys.argv[10]) if len(sys.argv) > 10 else 0.4
k = 72 / 25.4; s = 4
W, H = int((x1 - x0) * s), int((y1 - y0) * s)
im = Image.new('RGB', (W, H), 'white'); d = ImageDraw.Draw(im)
for dr in fitz.open(pdf)[page - 1].get_drawings():
    if (dr.get('width') or 0) < minpen or dr.get('dashes') not in (None, '[] 0'): continue
    for it in dr['items']:
        segs = []
        if it[0] == 'l': segs = [(it[1], it[2])]
        elif it[0] == 're':
            r = it[1]; c = [fitz.Point(r.x0, r.y0), fitz.Point(r.x1, r.y0), fitz.Point(r.x1, r.y1), fitz.Point(r.x0, r.y1)]
            segs = [(c[i], c[(i + 1) % 4]) for i in range(4)]
        for a, b in segs:
            d.line([((a.x / k - x0) * s, (a.y / k - y0) * s), ((b.x / k - x0) * s, (b.y / k - y0) * s)], fill='black', width=1)
ImageDraw.floodfill(im, (1, 1), (255, 200, 200))
inside = im.getpixel((int((ix - x0) * s), int((iy - y0) * s)))
print('inside point reached by the outside flood' if inside == (255, 200, 200) else 'inside point NOT reached: the linework closes')
d.ellipse([((ix - x0) * s - 6, (iy - y0) * s - 6), ((ix - x0) * s + 6, (iy - y0) * s + 6)], outline='blue', width=3)
im.save(out)
