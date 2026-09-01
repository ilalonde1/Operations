"""Render one page of a PDF, annotations included, to PNG."""
import sys

import fitz

doc = fitz.open(sys.argv[1])
page = doc[int(sys.argv[2]) - 1]
zoom = float(sys.argv[4]) if len(sys.argv) > 4 else 1.4
pix = page.get_pixmap(matrix=fitz.Matrix(zoom, zoom), annots=True)
pix.save(sys.argv[3])
print(f"{sys.argv[3]}  {pix.width}x{pix.height}")
