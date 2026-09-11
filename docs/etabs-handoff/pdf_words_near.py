"""Every line of a PDF's text that mentions any of the given words, with the pages it is on.

    python docs/etabs-handoff/pdf_words_near.py <pdf> WORD [WORD ...]

Case-insensitive; a regex is a word too (quote it). Prints "count pages text", most-repeated first, one
line per distinct text, so a phrase the set repeats on every section ("T/O ELEV. OVERRUN") stands out
from one that appears once. A reading instrument for the question "what does the drawing CALL this?"
before any rule is written about it (CLAUDE.md rule 9: ground truth first).

Built 2026-09-10 to learn what 31170's sections name L7 and L8 — the ROOF PLAN was landing on the
elevator overrun — and kept because the same question comes up on every new set.
"""
import re
import sys

import pypdf


def main():
    pdf, words = sys.argv[1], sys.argv[2:]
    rx = re.compile("|".join(words), re.I)
    reader = pypdf.PdfReader(pdf)
    hits = {}
    for i, page in enumerate(reader.pages, 1):
        for line in (page.extract_text() or "").splitlines():
            line = line.strip()
            if line and rx.search(line):
                hits.setdefault(line, set()).add(i)
    for text, pages in sorted(hits.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        ps = sorted(pages)
        shown = ",".join(map(str, ps[:8])) + ("..." if len(ps) > 8 else "")
        print(f"{len(ps):3}  p{shown:24}  {text[:110]}")


main()
