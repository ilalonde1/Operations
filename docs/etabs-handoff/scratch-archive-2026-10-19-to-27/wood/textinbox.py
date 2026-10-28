r"""textinbox.py <dxf> x0 y0 x1 y1 : the text entities (TEXT, MTEXT) of one DXF whose insert point lies in the box (mm), by
layer, and every layer with entities in the box. COVERS: what the ingestor wrote into the DXF. DOES NOT: the PDF's own words
(a DXF may carry none of them)."""
import collections, sys
import ezdxf

doc = ezdxf.readfile(sys.argv[1]); msp = doc.modelspace()
x0, y0, x1, y1 = map(float, sys.argv[2:6])
inside = lambda x, y: x0 <= x <= x1 and y0 <= y <= y1
texts = []; layers = collections.Counter()
for e in msp:
    t = e.dxftype()
    if t in ("TEXT", "MTEXT"):
        p = e.dxf.insert
        if inside(p.x, p.y): texts.append((e.dxf.layer, (e.text if t == "MTEXT" else e.dxf.text)[:60]))
    elif t == "LINE":
        if inside(e.dxf.start.x, e.dxf.start.y): layers[e.dxf.layer] += 1
    elif t in ("LWPOLYLINE", "POLYLINE"):
        pts = list(e.get_points() if t == "LWPOLYLINE" else e.points())
        if pts and inside(pts[0][0], pts[0][1]): layers[e.dxf.layer] += 1
print(f"{len(texts)} text(s) in the box")
for layer, s in texts[:40]: print(f"   {layer:<20} {s}")
print("layers with linework in the box:", dict(layers.most_common()))
print("all text entities in the file:", sum(1 for e in msp if e.dxftype() in ("TEXT", "MTEXT")))
