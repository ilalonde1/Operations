"""Makes KOR Remote's branding images from the app's logo (Kor.Operations.App/Resources/kor-logo.png).

The logo is white and orange on KOR navy (#435363). MeshCentral shows its pictures on its own backgrounds, so the
navy is taken out: each pixel's alpha is how far it is from navy, and its colour is un-mixed from navy at that alpha,
so the anti-aliased edges stay clean on any background.

  kor-remote-logo.png    the full logo, transparent          (titlepicture: the header; loginpicture: the login card)

Run from this folder: python make-branding.py
"""
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
SOURCE = HERE.parents[3] / "Kor.Operations.App" / "Resources" / "kor-logo.png"
NAVY = (0x43, 0x53, 0x63)


def without_navy(img: Image.Image) -> Image.Image:
    src = img.convert("RGB")
    out = Image.new("RGBA", src.size)
    px, po = src.load(), out.load()
    for y in range(src.height):
        for x in range(src.width):
            r, g, b = px[x, y]
            d = max(abs(r - NAVY[0]), abs(g - NAVY[1]), abs(b - NAVY[2]))
            a = min(1.0, d / 140.0)
            if a < 0.04:
                po[x, y] = (0, 0, 0, 0)
                continue
            un = tuple(max(0, min(255, round((c - (1 - a) * n) / a))) for c, n in zip((r, g, b), NAVY))
            po[x, y] = (*un, round(a * 255))
    return out


def main() -> None:
    clear = without_navy(Image.open(SOURCE))

    logo = clear.crop(clear.getbbox())
    logo.thumbnail((620, 240), Image.LANCZOS)
    logo.save(HERE / "kor-remote-logo.png", optimize=True)
    print("kor-remote-logo.png", logo.size)

    # The header mark: the circle and "KorStructural" only. The tagline under them is unreadable at header height.
    # It starts below the circle, so everything under the circle's lowest row is cut.
    w, h = clear.size
    alpha = clear.getchannel("A")
    circle_bottom = max(y for y in range(h) if any(alpha.getpixel((x, y)) > 128 for x in range(0, int(w * 0.25))))
    mark = clear.crop((0, 0, w, circle_bottom + 2))
    mark = mark.crop(mark.getbbox())
    mark.thumbnail((480, 120), Image.LANCZOS)
    mark.save(HERE / "kor-remote-mark.png", optimize=True)
    print("kor-remote-mark.png", mark.size)


if __name__ == "__main__":
    main()
