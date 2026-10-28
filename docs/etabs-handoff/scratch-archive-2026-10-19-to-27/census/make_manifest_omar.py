"""Manifest of Omar Alcazar's marked-up PDFs with shape markups, relative to E:\\Projects\\Projects."""
import csv
CORPUS = r"C:\VIsual Studio Projects\KOR.Drafter\intake\markup-harvest\MARKUP-CORPUS.csv"
OUT = r"C:\Users\ilalonde\AppData\Local\Temp\claude\C--VIsual-Studio-Projects-Operations\d8312bb3-eb17-42ed-b4ca-1ec6d7f8399a\scratchpad\census\manifest-omar.txt"
PREFIX = "\\\\kor-fs01\\projects\\projects\\"
rows = list(csv.DictReader(open(CORPUS, encoding="utf-8", errors="replace")))
out = [r["path"][len(PREFIX):] for r in rows
       if "omar alcazar" in (r["authors"] or "").lower()
       and ("Polygon=" in r["types"] or "Square=" in r["types"])
       and r["path"].lower().startswith(PREFIX)]
open(OUT, "w", encoding="utf-8").write("\n".join(out))
print(len(out), "of Omar's files carry shape markups")
