import fitz, sys
doc = fitz.open(sys.argv[1]); p = doc[2]
for x0, y0, x1, y1, w, b, l, n in p.get_text("words"):
    if 100 < x0 < 520 and w.upper() in ("PARKING", "BICYCLE", "LEVEL") or (100 < x0 < 520 and 1620 < y1 < 1670):
        print(f"{w!r} x {x0:.0f}-{x1:.0f} y {y1:.0f} h {y1-y0:.1f}")
r = fitz.Rect(90, 0, 560, 1728)
pix = p.get_pixmap(clip=fitz.Rect(90, 200, 1000, 1700), dpi=60); pix.save("p3-left.png")
