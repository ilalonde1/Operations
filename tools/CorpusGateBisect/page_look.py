r"""page_look.py <page.png> <dpi> <scale-denominator> <x_mm,y_mm> [...] : the PDF page itself around model points - what the drawing
draws there, which twin_look.py (the reader's DXF linework) cannot show when the question is whether the READER got it right.

<page.png> is `takeoff render <pdf> <dir> <dpi> <page> <page>`; the scale denominator is sheets.csv's scale_denominator (96 for
1/8" = 1'-0"). Writes <page>-crop-<x>-<y>.png (a 3,000 mm square, red + at the point) and <page>-overview.png (the whole page with
a red box at each point).

WHY (2026-10-27, board 49). 31138's three L2 OFF HER STACK columns traced to S2.12_2 (LEVEL 1 AT 55'-0); its DXF showed one large
outline at two of them and a sliver at the third, and only the page could say which was drawn and which was read: two are drawn
transitions (a solid shaded column over a dashed column below), the third is GC12 cut at the dashed edge of the column below.

⚠ IT ASSUMES the view's DXF keeps the page origin (bottom-left) at the sheet scale - true of a view whose sheets.csv placement is
the identity (rot=0;dx=0;dy=0;scale=1;ox=0;oy=0) and of nothing else. So the overview is not optional: LOOK that every box sits on
the thing asked about before reading a crop.
COVERS: one page, points in that frame. DOES NOT: apply a non-identity placement; rotated sheets; a view cropped from a larger page.
SAME-CLASS FAULT IT MISSES: a placed view (two views on a sheet, the second offset) - its crops land on the wrong part of the page
and look plausible, which is why the overview is drawn.
Exit 0; 2 on bad arguments.
"""
import sys
from PIL import Image, ImageDraw

if len(sys.argv) < 5:
    print(__doc__); sys.exit(2)
img = Image.open(sys.argv[1]).convert("RGB")
dpi, denom = float(sys.argv[2]), float(sys.argv[3])
W, H = img.size
k = dpi / 25.4 / denom          # page px per model mm: one page inch is <denom> model inches
half = int(1500 * k)
ov = img.copy(); d = ImageDraw.Draw(ov)
for a in sys.argv[4:]:
    x, y = map(float, a.split(",")); px, py = int(x * k), int(H - y * k)
    c = img.crop((px - half, py - half, px + half, py + half)).resize((900, 900))
    cd = ImageDraw.Draw(c); cd.line((440, 450, 460, 450), fill=(255, 0, 0), width=2); cd.line((450, 440, 450, 460), fill=(255, 0, 0), width=2)
    out = sys.argv[1].replace(".png", f"-crop-{int(x)}-{int(y)}.png"); c.save(out); print(out, px, py)
    d.rectangle((px - half, py - half, px + half, py + half), outline=(255, 0, 0), width=12)
ov.resize((W // 6, H // 6)).save(sys.argv[1].replace(".png", "-overview.png"))
print(sys.argv[1].replace(".png", "-overview.png"))
