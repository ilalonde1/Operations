"""KOR Remote's device icons: a white line glyph on a KOR-navy rounded tile, replacing MeshCentral's 2008-era pictures.

MeshCentral draws device icons from sprite strips (8 icons side by side) and, on a device's page, one 256 px picture
per type. Files of the same name under /opt/meshcentral/meshcentral-web/public/images/ are served instead of its own:
  icons16.png 128x16 · icons32.png 256x32 · icons50.png 400x50 · icons100.png 800x100 · icons256-<n>-1.png 256x256
Order (MeshCentral's): 1 desktop, 2 laptop, 3 phone, 4 server, 5 storage, 6 router, 7 embedded board, 8 virtual machine.

Run from this folder: python make-icons.py   (writes into ./icons/)
"""
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
OUT = HERE / "icons"
NAVY = (0x43, 0x53, 0x63, 255)
WHITE = (255, 255, 255, 255)
ORANGE = (0xFF, 0x5B, 0x35, 255)
S = 1024                      # drawn large, then scaled down: smooth edges at every size
W = 56                        # glyph line width at 1024


def tile() -> tuple[Image.Image, ImageDraw.ImageDraw]:
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((40, 40, S - 40, S - 40), radius=210, fill=NAVY)
    return im, d


def monitor(d, box, stand=True):
    x0, y0, x1, y1 = box
    d.rounded_rectangle(box, radius=40, outline=WHITE, width=W)
    if stand:
        cx = (x0 + x1) // 2
        d.line((cx, y1, cx, y1 + 90), fill=WHITE, width=W)
        d.line((cx - 140, y1 + 110, cx + 140, y1 + 110), fill=WHITE, width=W)


def desktop():
    im, d = tile(); monitor(d, (230, 250, 794, 640)); return im


def laptop():
    im, d = tile()
    d.rounded_rectangle((270, 260, 754, 600), radius=36, outline=WHITE, width=W)
    d.rounded_rectangle((190, 640, 834, 720), radius=36, fill=WHITE)
    return im


def phone():
    im, d = tile()
    d.rounded_rectangle((370, 200, 654, 824), radius=60, outline=WHITE, width=W)
    d.ellipse((482, 700, 542, 760), fill=WHITE)
    return im


def server():
    im, d = tile()
    for y in (230, 430, 630):
        d.rounded_rectangle((250, y, 774, y + 160), radius=34, outline=WHITE, width=W)
        d.ellipse((330, y + 55, 380, y + 105), fill=ORANGE)
        d.line((450, y + 80, 690, y + 80), fill=WHITE, width=W // 2)
    return im


def storage():
    im, d = tile()
    d.rounded_rectangle((220, 330, 804, 690), radius=60, outline=WHITE, width=W)
    d.line((220, 510, 804, 510), fill=WHITE, width=W // 2)
    for y in (420, 600):
        d.ellipse((680, y - 25, 730, y + 25), fill=ORANGE)
    return im


def router():
    im, d = tile()
    d.rounded_rectangle((210, 480, 814, 700), radius=50, outline=WHITE, width=W)
    for x in (340, 684):
        d.line((x, 480, x - 40 if x < 512 else x + 40, 270), fill=WHITE, width=W // 2 + 8)
    for x in (330, 420, 510):
        d.ellipse((x, 565, x + 50, 615), fill=ORANGE)
    return im


def embedded():
    im, d = tile()
    d.rounded_rectangle((330, 330, 694, 694), radius=40, outline=WHITE, width=W)
    for i in range(3):
        o = 400 + i * 112
        for a, b in (((o, 250), (o, 330)), ((o, 694), (o, 774)), ((250, o), (330, o)), ((694, o), (774, o))):
            d.line((*a, *b), fill=WHITE, width=W // 2)
    d.rounded_rectangle((440, 440, 584, 584), radius=20, fill=ORANGE)
    return im


def virtual():
    im, d = tile()
    d.rounded_rectangle((330, 210, 800, 520), radius=36, outline=(255, 255, 255, 150), width=W // 2 + 6)
    monitor(d, (220, 330, 690, 640))
    return im


ICONS = [desktop, laptop, phone, server, storage, router, embedded, virtual]


def main() -> None:
    OUT.mkdir(exist_ok=True)
    big = [f() for f in ICONS]
    for size, name in ((16, "icons16.png"), (32, "icons32.png"), (50, "icons50.png"), (100, "icons100.png")):
        strip = Image.new("RGBA", (size * 8, size), (0, 0, 0, 0))
        for i, im in enumerate(big):
            strip.paste(im.resize((size, size), Image.LANCZOS), (i * size, 0))
        strip.save(OUT / name, optimize=True)
        # The same strip under a KOR name too: custom.css points the icon classes at it, so a browser that cached
        # MeshCentral's own icons*.png cannot keep showing the old pictures.
        strip.save(OUT / f"kor-{name}", optimize=True)
        print(name, strip.size)
    for i, im in enumerate(big, start=1):
        im.resize((256, 256), Image.LANCZOS).save(OUT / f"icons256-{i}-1.png", optimize=True)
    print("icons256-1..8-1.png (256, 256)")


if __name__ == "__main__":
    main()
