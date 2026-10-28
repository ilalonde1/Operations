"""Draw every POLYLINE in an R12 DXF, coloured by layer, plus TEXT positions. usage: render_dxf.py <in.dxf> <out.png>"""
import sys
from PIL import Image, ImageDraw

pairs = [l.strip() for l in open(sys.argv[1], encoding='cp1252').read().splitlines()]
it = iter(zip(pairs[0::2], pairs[1::2]))
polys, texts, cur, layer, ent = [], [], None, '', None
x = y = None
for code, val in it:
    if code == '0':
        if ent == 'VERTEX' and cur is not None and x is not None: cur[1].append((x, y))
        if ent == 'TEXT' and x is not None: texts.append((x, y))
        ent, x, y = val, None, None
        if val == 'POLYLINE': cur = [None, []]; polys.append(cur)
        if val == 'SEQEND': cur = None
    elif code == '8' and ent in ('POLYLINE', 'TEXT'):
        if ent == 'POLYLINE': cur[0] = val
    elif code == '10': x = float(val)
    elif code == '20': y = float(val)
pts = [p for _, ps in polys for p in ps] + texts
minx, maxx = min(p[0] for p in pts), max(p[0] for p in pts)
miny, maxy = min(p[1] for p in pts), max(p[1] for p in pts)
W = 900; s = W / max(maxx - minx, maxy - miny); H = int((maxy - miny) * s) + 40
img = Image.new('RGB', (int((maxx - minx) * s) + 40, H), 'white'); d = ImageDraw.Draw(img)
col = {'SLAB-MARKUP': (0, 90, 200), 'COLUMN-MARKUP': (170, 0, 0), 'BEAM-MARKUP': (0, 150, 0)}
for lay, ps in polys:
    q = [(20 + (px - minx) * s, H - 20 - (py - miny) * s) for px, py in ps]
    if len(q) > 1: d.line(q + [q[0]] if lay != 'BEAM-MARKUP' else q, fill=col.get(lay, (0, 0, 0)), width=2)
for tx, ty in texts:
    d.ellipse([20 + (tx - minx) * s - 4, H - 20 - (ty - miny) * s - 4, 20 + (tx - minx) * s + 4, H - 20 - (ty - miny) * s + 4], outline=(255, 0, 255))
img.save(sys.argv[2])
print(f"polylines {len(polys)}  texts {len(texts)}  extent {(maxx-minx)/1000:.2f} x {(maxy-miny)/1000:.2f} m")
