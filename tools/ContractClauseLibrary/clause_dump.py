"""Full text of the key clauses for the documents that matter most: every executed document plus
named benchmarks. Usage: python -I clause_dump.py <lib-dir> [extra key substrings...] > dump.txt"""
import csv, re, sys
from collections import defaultdict
lib_dir = sys.argv[1]
extra = [s.lower() for s in sys.argv[2:]]
csv.field_size_limit(10_000_000)
rows = list(csv.DictReader(open(f"{lib_dir}/library_clauses.csv", encoding="utf-8")))
TOPICS = ["limitation_of_liability", "consequential_damages", "indemnity", "intellectual_property", "insurance",
          "termination", "suspension", "payment", "standard_of_care", "warranty_reperform"]
HEAD = {
    "limitation_of_liability": r"limitation of liability|amount of damages|limit",
    "consequential_damages": r"consequential|excluded liability",
    "indemnity": r"indemnif",
    "intellectual_property": r"intellectual property|copyright|ownership|licen[cs]e|instruments of service|work product",
    "insurance": r"insurance",
    "termination": r"terminat",
    "suspension": r"suspen|stand-?down",
    "payment": r"payment|invoice|price|fees|compensation|withh",
    "standard_of_care": r"standard of (care|service)|professional",
    "warranty_reperform": r"warrant|re-?perform",
}
docs = defaultdict(list)
for r in rows:
    key = (r["Key"], r["File"], r["Part"])
    sel = r["Role"] == "executed" or any(e in (r["Key"] + r["File"] + r["ClientName"]).lower() for e in extra)
    if sel and r["Paper"] == "client":
        docs[key].append(r)
seen_text = set()
for (key, file, part), rs in sorted(docs.items(), key=lambda kv: (kv[1][0]["ClientName"], kv[0])):
    r0 = rs[0]
    print("#" * 100)
    print(f"{r0['ClientName'] or '(no Deltek client)'} | {key} | {r0['Role']} | v{r0['Version']} | {r0['Date']} | {file[-80:]}{(' > ' + part[:40]) if part else ''}")
    for topic in TOPICS:
        cands = [r for r in rs if topic in r["Topics"].split("|")]
        if not cands:
            continue
        best = sorted(cands, key=lambda r: (0 if re.search(HEAD[topic], r["Heading"], re.I) else 1, -int(r["Words"])))[0]
        t = re.sub(r"\s+", " ", best["Text"]).strip()
        sig = (topic, t[:200])
        if sig in seen_text:
            print(f"  [{topic}] §{best['Section']} (same text as an earlier document)")
            continue
        seen_text.add(sig)
        print(f"  [{topic}] §{best['Section']} {best['Heading'][:60]} ({best['Words']} w)")
        print(f"      {t[:1500]}")
