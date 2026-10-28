"""Every bar call-out (e.g. 15M@300, 20M3600@300, 4-C35M7000) on a page of each issue, counted, and the difference.
A plain independent reading of the PDF text - no repo code. Args: before.pdf after.pdf page [page ...] (1-based)."""
import re, sys, fitz
from collections import Counter

BAR = re.compile(r"\b\d{2}M(?:\d{3,5})?@\d{2,4}\b|\b\d+-?C?\d{2}M\d{3,5}\b")

def callouts(path, page):
    p = fitz.open(path)[page - 1]
    # words joined per line, so "15M @ 300" split across words still reads
    text = p.get_text("text")
    return Counter(m.group(0) for m in BAR.finditer(text.replace(" @ ", "@").replace("@ ", "@").replace(" @", "@")))

before, after = sys.argv[1], sys.argv[2]
for page in map(int, sys.argv[3:]):
    b, a = callouts(before, page), callouts(after, page)
    added, removed = a - b, b - a
    print(f"page {page}: before {sum(b.values())} call-outs, after {sum(a.values())}"
          f"   added {dict(added) or '-'}   removed {dict(removed) or '-'}")
