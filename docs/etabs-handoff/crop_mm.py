"""Crop a pdf-overlay PNG rendered at DPI around a point given in drawing millimetres.
usage: crop_mm.py <png> <dpi> <scale> <xmm> <ymm> <halfWmm> <halfHmm> <out>"""
import sys
from PIL import Image
png, dpi, scale, x, y, hw, hh, out = sys.argv[1:9]
dpi, scale, x, y, hw, hh = float(dpi), float(scale), float(x), float(y), float(hw), float(hh)
k = dpi / (scale * 25.4)
im = Image.open(png)
H = im.size[1]; box = (int((x - hw) * k), int(H - (y + hh) * k), int((x + hw) * k), int(H - (y - hh) * k))
box = (max(0, box[0]), max(0, box[1]), min(im.size[0], box[2]), min(im.size[1], box[3]))
im.crop(box).save(out)
print(out, box)
