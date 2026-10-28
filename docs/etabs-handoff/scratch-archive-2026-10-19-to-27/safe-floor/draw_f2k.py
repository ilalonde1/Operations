# Draws an .f2k from its own tables (no model, no writer code): floors blue, openings white with an X,
# wall/beam lines red, columns green squares sized by their section. Usage: draw_f2k.py out.png title f2k [f2k ...]
import re, sys
from PIL import Image, ImageDraw

def tables(path):
    t, cur = {}, None
    for line in open(path, encoding='ascii', errors='replace'):
        m = re.match(r'^TABLE:\s+"([^"]+)"', line)
        if m:
            cur = t.setdefault(m.group(1), [])
            continue
        if cur is not None and line.strip():
            cur.append(line)
    return t

def field(row, name):
    m = re.search(r'(?<![\w"])' + re.escape(name) + r'=("[^"]*"|\S+)', row)
    return m.group(1).strip('"') if m else None

def model(path):
    t = tables(path)
    pts = {field(r, 'UniqueName'): (float(field(r, 'X')), float(field(r, 'Y'))) for r in t.get('POINT OBJECT CONNECTIVITY', [])}
    floors = {}
    for r in t.get('FLOOR OBJECT CONNECTIVITY', []):
        name = field(r, '"Unique Name"')
        for i in range(1, 5):
            p = field(r, f'UniquePt{i}')
            if p: floors.setdefault(name, []).append(pts[p])
    openings = []
    for r in t.get('FLOOR OBJECT OPENINGS', []):
        openings.append([pts[field(r, f'UniquePt{i}')] for i in range(1, 200) if field(r, f'UniquePt{i}')])
    lines = [(pts[field(r, 'UniquePtI')], pts[field(r, 'UniquePtJ')]) for r in t.get('BEAM OBJECT CONNECTIVITY', [])]
    secs = {}
    for r in t.get('FRAME SECTION PROPERTY DEFINITIONS - CONCRETE RECTANGULAR', []):
        secs[field(r, 'Name')] = (float(field(r, 'Width')), float(field(r, 'Depth')))
    cols = [(pts[field(r, 'UniqueName')], secs.get(field(r, 'SecName'), (400, 400))) for r in t.get('POINT ASSIGNMENTS - COLUMN BELOW', [])]
    return floors, openings, lines, cols

out, title, files = sys.argv[1], sys.argv[2], sys.argv[3:]
cell = 900
img = Image.new('RGB', (cell * len(files), cell + 40), 'white')
d = ImageDraw.Draw(img)
d.text((10, 8), title, fill='black')
for k, f in enumerate(files):
    floors, openings, lines, cols = model(f)
    xs = [p[0] for ring in floors.values() for p in ring] + [p[0] for (p, _) in cols]
    ys = [p[1] for ring in floors.values() for p in ring] + [p[1] for (p, _) in cols]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    s = (cell - 60) / max(x1 - x0, y1 - y0)
    ox = k * cell + 30
    P = lambda p: (ox + (p[0] - x0) * s, 40 + 20 + (y1 - p[1]) * s)
    for ring in floors.values():
        d.polygon([P(p) for p in ring], fill=(207, 225, 242), outline=(60, 110, 170))
    for ring in openings:
        d.polygon([P(p) for p in ring], fill='white', outline='black')
        d.line([P(ring[0]), P(ring[2])], fill='gray'); d.line([P(ring[1]), P(ring[3])], fill='gray')
    for a, b in lines:
        d.line([P(a), P(b)], fill=(200, 30, 30), width=3)
    for (p, (w, dd)) in cols:
        cx, cy = P(p); hw, hd = max(2, w * s / 2), max(2, dd * s / 2)
        d.rectangle([cx - hw, cy - hd, cx + hw, cy + hd], fill=(30, 140, 60))
    d.text((ox, 40), f.replace('\\', '/').split('/')[-1] + f"  floors {len(floors)} openings {len(openings)} lines {len(lines)} columns {len(cols)}", fill='black')
img.save(out)
print(out)
