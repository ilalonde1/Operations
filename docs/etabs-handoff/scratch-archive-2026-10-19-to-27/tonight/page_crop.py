r"""page_crop.py <page.png> <dpi> <mm-per-pt> <x_mm,y_mm> [...] : crops a rendered PDF page around model points, ASSUMING the
view's DXF keeps the page origin (bottom-left) at mm-per-pt - a guess this checks by a marked overview, not a fact.
Writes <page>-crop-<x>-<y>.png (a 3,000 mm square, + at the point) and <page>-overview.png (whole page, red boxes at the points)."""
import sys
from PIL import Image, ImageDraw
img = Image.open(sys.argv[1]).convert("RGB"); dpi = float(sys.argv[2]); mmpt = float(sys.argv[3])
W, H = img.size; k = dpi / 72.0 / mmpt          # px per mm
half = int(1500 * k)
ov = img.copy(); d = ImageDraw.Draw(ov)
for a in sys.argv[4:]:
    x, y = map(float, a.split(",")); px, py = int(x * k), int(H - y * k)
    c = img.crop((px - half, py - half, px + half, py + half)).resize((900, 900))
    cd = ImageDraw.Draw(c); cd.line((440, 450, 460, 450), fill=(255, 0, 0), width=2); cd.line((450, 440, 450, 460), fill=(255, 0, 0), width=2)
    out = sys.argv[1].replace(".png", f"-crop-{int(x)}-{int(y)}.png"); c.save(out); print(out, px, py)
    d.rectangle((px - half, py - half, px + half, py + half), outline=(255, 0, 0), width=12)
ov.resize((W // 6, H // 6)).save(sys.argv[1].replace(".png", "-overview.png"))
