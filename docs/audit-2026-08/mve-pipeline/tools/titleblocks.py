"""Render the bottom-right title-block corner of each plan sheet and stack
them into one contact sheet, so every consultant stamp can be read in one look.
"""
import os, subprocess, sys
from PIL import Image

pdf = sys.argv[1]
pages = [int(p) for p in sys.argv[2].split(",")]
out = sys.argv[3]

# rendered page at 150 dpi is ~2550 x 1650 (17x11 landscape)
X, Y, W, H = 1950, 1400, 600, 250

tiles = []
for p in pages:
    stem = f"tb_{p}"
    subprocess.run(["pdftoppm", "-png", "-r", "150", "-f", str(p), "-l", str(p),
                    "-x", str(X), "-y", str(Y), "-W", str(W), "-H", str(H),
                    pdf, stem], check=True)
    hit = [f for f in os.listdir(".") if f.startswith(stem + "-") and f.endswith(".png")]
    if not hit:
        continue
    im = Image.open(hit[0]).convert("RGB")
    tiles.append((p, im))

if not tiles:
    raise SystemExit("no tiles")

tw = max(im.width for _, im in tiles)
th = max(im.height for _, im in tiles)
cols = 3
rows = (len(tiles) + cols - 1) // cols
sheet = Image.new("RGB", (cols * tw, rows * th), "white")
for i, (p, im) in enumerate(tiles):
    sheet.paste(im, ((i % cols) * tw, (i // cols) * th))
sheet.save(out)
print("pages:", [p for p, _ in tiles], "->", out, sheet.size)
