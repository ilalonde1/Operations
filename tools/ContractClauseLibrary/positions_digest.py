"""Digest of KOR's positions on file: for every executed or latest-on-file client-paper document and each
key topic, the clause whose heading names the topic (else the first tagged clause), with the dollar /
multiplier phrases it carries. Usage: python -I positions_digest.py <lib-dir> > digest.txt"""
import csv, re, sys
from collections import defaultdict
lib_dir = sys.argv[1]
csv.field_size_limit(10_000_000)
rows = list(csv.DictReader(open(f"{lib_dir}/library_clauses.csv", encoding="utf-8")))
KEY_TOPICS = {
    "limitation_of_liability": r"limitation of liability|amount of damages|limit(ation)? on liability|liability",
    "indemnity": r"indemnif",
    "consequential_damages": r"consequential",
    "intellectual_property": r"intellectual property|copyright|ownership|instruments of service|documents",
    "insurance": r"insurance",
    "termination": r"terminat",
    "suspension": r"suspen|stand-?down",
    "payment": r"payment|invoice|fees|compensation",
    "standard_of_care": r"standard of care|professional",
}
MONEY = re.compile(r"\$\s?[\d,]{4,}(\.\d+)?|\b(two|three|2|3|1\.5)\s*(times|x)\b|twice|greater of|lesser of|the (total |aggregate )?(fee|price|compensation)s?\b[^.]{0,30}", re.I)
docs = defaultdict(list)
for r in rows:
    if r["Paper"] != "client" or not (r["Role"] == "executed" or r["Latest"] == "yes"):
        continue
    docs[(r["Key"], r["File"], r["Part"])].append(r)
print(f"documents: {len(docs)}")
for (key, file, part), rs in sorted(docs.items(), key=lambda kv: (kv[1][0]["ClientName"], kv[0])):
    r0 = rs[0]
    print("=" * 110)
    print(f"{r0['ClientName'] or '(no Deltek client)'} | {key} | {r0['Role']} | v{r0['Version']} | {r0['Date']} | {file[-70:]}{(' > ' + part[:30]) if part else ''}")
    for topic, head_rx in KEY_TOPICS.items():
        cands = [r for r in rs if topic in r["Topics"].split("|")]
        if not cands:
            continue
        best = next((r for r in cands if re.search(head_rx, r["Heading"], re.I)), cands[0])
        t = re.sub(r"\s+", " ", best["Text"])
        money = "; ".join(sorted({m.group(0).strip() for m in MONEY.finditer(t)}))[:120]
        print(f"  [{topic}] §{best['Section']} {best['Heading'][:50]} ({best['Words']} w){(' | $: ' + money) if money else ''}")
        print(f"      {t[:420]}")
