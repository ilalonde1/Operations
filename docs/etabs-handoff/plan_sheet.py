"""Draw every storey of an .e2k as one sheet of plans, so a person can look at it.

    python plan_sheet.py model.e2k out.svg "title" [columns] [cell px]

Then open the SVG, or screenshot it:
    msedge --headless=new --screenshot=out.png --window-size=1330,1780 file:///.../wrapper.html
(a bare .svg writes nothing; wrap it in an <img> in an .html, and the PNG lands a second
after the process exits.)

THIS IS THE CHECK THAT WORKS. On 27 August a day of count tables missed eight faults that one
rendering showed at once: storeys carrying a floor with nothing under it, storeys with structure
and no floor, one tower's storey holding both towers' columns, a site-wide slab under a single
building. Every one of them had been found until then by the engineer opening the file.

Counts cannot see a floor in the wrong place, a slab with the whole site under it, or columns
standing in open air. A picture can, in about ten seconds.

Each plate is drawn in its own colour on purpose: filled alike, two slabs that abut read as one
self-crossing ring, and LEVEL 2's three slabs were misread that way for half an hour.
"""
import io, re, sys, collections

def read(path):
    pts, area, line = {}, {}, {}
    kind = {}
    on = collections.defaultdict(list)
    order = []
    for raw in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'\s*STORY\s+"([^"]+)"', raw)
        if m:
            order.append(m.group(1)); continue
        m = re.match(r'\s*POINT\s+"([^"]+)"\s+(-?[\d.eE+]+)\s+(-?[\d.eE+]+)', raw)
        if m:
            pts[m.group(1)] = (float(m.group(2)), float(m.group(3))); continue
        m = re.match(r'\s*AREA\s+"([^"]+)"\s+(\w+)\s+(\d+)\s+(.*)$', raw)
        if m:
            names = re.findall(r'"([^"]+)"', m.group(4))[:int(m.group(3))]
            area[m.group(1)] = [pts[n] for n in names if n in pts]
            kind[m.group(1)] = m.group(2); continue
        m = re.match(r'\s*LINE\s+"([^"]+)"\s+(\w+)\s+"([^"]+)"\s+"([^"]+)"', raw)
        if m:
            if m.group(3) in pts and m.group(4) in pts:
                line[m.group(1)] = [pts[m.group(3)], pts[m.group(4)]]
            kind[m.group(1)] = m.group(2); continue
        m = re.match(r'\s*(?:AREA|LINE)ASSIGN\s+"([^"]+)"\s+"([^"]+)"', raw)
        if m:
            on[m.group(2)].append(m.group(1))
    return order, area, line, kind, on

def svg(path, out, title):
    order, area, line, kind, on = read(path)
    every = [p for v in area.values() for p in v] + [p for v in line.values() for p in v]
    if not every: return
    minx = min(p[0] for p in every); maxx = max(p[0] for p in every)
    miny = min(p[1] for p in every); maxy = max(p[1] for p in every)
    w, h = maxx - minx, maxy - miny
    live = [s for s in order if on.get(s)]
    cols = int(sys.argv[4]) if len(sys.argv) > 4 else 4
    cell, pad, head = (int(sys.argv[5]) if len(sys.argv) > 5 else 300), 10, 18
    rows = (len(live) + cols - 1) // cols
    sc = min(cell / w, cell / h) if w and h else 1

    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{cols*(cell+pad)+pad}" '
             f'height="{rows*(cell+pad+head)+pad+30}" style="background:#fff">',
             f'<text x="{pad}" y="20" font-family="monospace" font-size="15" font-weight="bold">{title}</text>']

    for i, storey in enumerate(live):
        ox = pad + (i % cols) * (cell + pad)
        oy = 30 + pad + (i // cols) * (cell + pad + head)
        objs = on[storey]
        nf = sum(1 for o in objs if kind.get(o) == 'FLOOR')
        nw = sum(1 for o in objs if kind.get(o) == 'PANEL')
        nc = sum(1 for o in objs if kind.get(o) == 'COLUMN')
        parts.append(f'<text x="{ox}" y="{oy-6}" font-family="monospace" font-size="11">'
                     f'{storey}  {nf}f {nw}w {nc}c</text>')
        parts.append(f'<rect x="{ox}" y="{oy}" width="{cell}" height="{cell}" fill="none" stroke="#ddd"/>')

        def xy(p):
            return (ox + (p[0] - minx) * sc, oy + cell - (p[1] - miny) * sc)

        # Each plate gets its own colour and a drawn edge. Filled alike, two slabs that abut
        # read as one shape -- LEVEL 2's three slabs looked like a single bow-tie ring.
        shades = ['#9ec5e8', '#f5c78a', '#a8dab5', '#d7b3e0', '#f2a7a7', '#b8c9e8']
        edges  = ['#2b6ca3', '#b3701a', '#2d7a45', '#7a3d8f', '#a83232', '#3a4f7a']
        nth = 0
        for o in objs:
            k = kind.get(o)
            if k == 'FLOOR' and o in area:
                d = ' '.join(f'{x:.1f},{y:.1f}' for x, y in map(xy, area[o]))
                parts.append(f'<polygon points="{d}" fill="{shades[nth % len(shades)]}" '
                             f'fill-opacity="0.5" stroke="{edges[nth % len(edges)]}" stroke-width="1.1"/>')
                nth += 1
        for o in objs:
            k = kind.get(o)
            if k == 'PANEL' and o in area:
                p = list(map(xy, area[o]))
                d = ' '.join(f'{x:.1f},{y:.1f}' for x, y in p)
                parts.append(f'<polyline points="{d}" fill="none" stroke="#c0392b" stroke-width="1.4"/>')
            elif k == 'COLUMN' and o in line:
                x, y = xy(line[o][0])
                parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="1.8" fill="#1e8449"/>')
            elif k in ('BEAM', 'BRACE') and o in line:
                a, b = map(xy, line[o])
                parts.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="#7d3c98" stroke-width="0.8"/>')

    parts.append('</svg>')
    io.open(out, 'w', encoding='utf-8').write('\n'.join(parts))
    print(f'{out}  ({len(live)} storeys drawn)')

svg(sys.argv[1], sys.argv[2], sys.argv[3])
