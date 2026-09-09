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
