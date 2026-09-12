"""How the corpus's plan sheets name their storeys — from the analyzer's sheet ledger, no PDF opened.

    python docs/etabs-handoff/corpus/plan_titles.py [ledger-sheets.csv]

Prints: plan pages with and without a level name from the reader, and for the ones without, the words
and whole titles most repeated — the vocabulary the storeys-from-plans rule has to speak. PROTOTYPE of
a `takeoff corpus-query` verb (2026-09-11); replaced by it when the ledger is in the DB (migration 083).
"""
import collections
import csv
import os
import re
import sys


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ["LOCALAPPDATA"], "Temp", "kor-drawings", "corpus", "ledger-sheets.csv")
    sheets = [r for r in csv.DictReader(open(path, encoding="utf-8-sig", newline="")) if r["sheet_type"] == "plan"]
    with_level = sum(1 for r in sheets if r["level"])
    print(f"{len(sheets)} plan pages; {with_level} carry a level name from the reader; {sum(1 for r in sheets if r['dxf_files'])} wrote a view")
    nolevel = [r for r in sheets if not r["level"]]
    words = collections.Counter()
    for r in nolevel:
        for w in re.findall(r"[A-Z][A-Z/]+", (r["title"] or "").upper()):
            if len(w) > 2:
                words[w] += 1
    print(f"{len(nolevel)} plans with no level name; their title words:")
    for w, n in words.most_common(40):
        print(f"   {n:5}  {w}")
    strip = re.compile(r"^[A-Z]+\d[\d.]*\s*-\s*")
    titles = collections.Counter(strip.sub("", (r["title"] or "").upper()).strip()[:44] for r in nolevel)
    print("most repeated titles among them:")
    for t, n in titles.most_common(30):
        print(f"   {n:5}  {t}")
    levels = collections.Counter(r["level"] for r in sheets if r["level"])
    print(f"level names the reader gave ({len(levels)} distinct); most common:")
    for l, n in levels.most_common(25):
        print(f"   {n:5}  {l}")


if __name__ == "__main__":
    main()
