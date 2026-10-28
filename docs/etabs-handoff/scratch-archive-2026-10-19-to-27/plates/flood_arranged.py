import sys, csv
from PIL import Image, ImageDraw
# flood_arranged.py <dump.csv> x0 y0 x1 y1 ix iy <out.png> : the arrangement's own edges (model mm) drawn at 40 mm a pixel,
# the outside flooded from the corner; layers coloured so a missing kind shows
f, out = sys.argv[1], sys.argv[8]
x0, y0, x1, y1, ix, iy = map(float, sys.argv[2:8])
s = 1 / 40.0
W, H = int((x1 - x0) * s), int((y1 - y0) * s)
im = Image.new('RGB', (W, H), 'white'); d = ImageDraw.Draw(im)
col = {'WALL': (120, 0, 0), 'DOOR': (0, 120, 0), 'MATCH': (0, 0, 200), 'BRIDGE': (255, 0, 255)}
P = lambda x, y: ((x - x0) * s, (y1 - y) * s)
for r in csv.reader(open(f)):
    d.line([P(float(r[1]), float(r[2])), P(float(r[3]), float(r[4]))], fill=col.get(r[0], (0, 0, 0)), width=1)
ImageDraw.floodfill(im, (1, 1), (255, 200, 200))
px = P(ix, iy)
print('inside reached by the outside: the arrangement leaks' if im.getpixel((int(px[0]), int(px[1]))) == (255, 200, 200) else 'inside closed')
d.ellipse([px[0] - 6, px[1] - 6, px[0] + 6, px[1] + 6], outline='blue', width=3)
im.save(out)
