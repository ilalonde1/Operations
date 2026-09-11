"""GROUND TRUTH for a row of grid bubbles: every word the PDF places in a horizontal band, with its
position — through fitz, no reader in the way.

    python docs/etabs-handoff/pdf_words_in_band.py <pdf> <page1based> <y0> <y1> [x0 x1]

Band in PDF points, y DOWN. Prints x, y, the text, and how many other words share its spot (within
3 pt) — two words on one spot is what a bubble drawn twice (an architect's underlay under the
engineer's grid) looks like, and GridBubbles wants exactly one word inside a circle.

Built 2026-09-11 when 31168's 2026-09-10 reissue read 9 of 28 grid axes on S2.04.1 and the 08-25
issue read 26 of 26.
"""
import sys

import fitz


def main():
    pdf, page_no, y0, y1 = sys.argv[1], int(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4])
    x0 = float(sys.argv[5]) if len(sys.argv) > 5 else -1e9
    x1 = float(sys.argv[6]) if len(sys.argv) > 6 else 1e9
    page = fitz.open(pdf)[page_no - 1]
    words = [(w[0], w[1], w[2], w[3], w[4]) for w in page.get_text("words")]
    band = [(round((a + c) / 2, 1), round((b + d) / 2, 1), t) for a, b, c, d, t in words if y0 <= (b + d) / 2 <= y1 and x0 <= (a + c) / 2 <= x1]
    band.sort()
    print(f"{len(band)} words in y {y0:.0f}..{y1:.0f}")
    for x, y, t in band:
        twins = sum(1 for x2, y2, t2 in band if abs(x2 - x) <= 3 and abs(y2 - y) <= 3) - 1
        print(f"  x {x:8.1f}  y {y:7.1f}  {t!r}" + (f"   +{twins} on the same spot" if twins else ""))


if __name__ == "__main__":
    main()
