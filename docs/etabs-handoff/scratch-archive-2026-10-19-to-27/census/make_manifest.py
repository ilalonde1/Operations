"""Manifest for ShareTextExtract --annotations on FS01: corpus PDFs with shape markups, relative to E:\\Projects\\Projects."""
import csv
CORPUS = r"C:\VIsual Studio Projects\KOR.Drafter\intake\markup-harvest\MARKUP-CORPUS.csv"
OUT = r"C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\census\manifest.txt"
PREFIX = "\\\\kor-fs01\\projects\\projects\\"
rows = list(csv.DictReader(open(CORPUS, encoding="utf-8", errors="replace")))
out, elsewhere = [], 0
for r in rows:
    if "Polygon=" not in r["types"] and "Square=" not in r["types"]:
        continue
    p = r["path"]
    if not p.lower().startswith(PREFIX):
        elsewhere += 1
        continue
    out.append(p[len(PREFIX):])
open(OUT, "w", encoding="utf-8").write("\n".join(out))
print(len(rows), "corpus files;", len(out), "with shape markups on the projects share;", elsewhere, "elsewhere (skipped)")
print(out[0])
print(out[-1])
