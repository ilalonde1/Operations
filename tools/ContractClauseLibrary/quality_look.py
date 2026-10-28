"""Look at what the library gate kept and dropped. Usage: python -I quality_look.py <inv-dir> <ext-dir> <lib-dir>"""
import csv, os, re, sys
from collections import Counter, defaultdict
inv_dir, ext_dir, lib_dir = sys.argv[1:4]
csv.field_size_limit(10_000_000)

def norm(p):
    return re.sub(r"^[A-Za-z]:/", "", p.replace("\\", "/")).lower()

inv = {norm(r["FullName"]): r for r in csv.DictReader(open(os.path.join(inv_dir, "inventory.csv"), encoding="utf-8"))}
docs = list(csv.DictReader(open(os.path.join(ext_dir, "documents.csv"), encoding="utf-8")))
lib = list(csv.DictReader(open(os.path.join(lib_dir, "library_clauses.csv"), encoding="utf-8")))

# documents that reached the library (by File+Part) and their roles
in_lib = {}
for r in lib:
    in_lib[(r["File"], r["Part"])] = r["Role"]

print("=== documents named executed/agreement that did NOT reach the library (role other) ===")
n = 0
for d in docs:
    if d["Part"] and d["Part"] != "body":
        continue
    r = inv.get(norm(d["RelPath"]), {})
    kinds = set((r.get("Kinds") or "").split("|")) - {""}
    if not (kinds & {"executed", "agreement"}):
        continue
    if (d["RelPath"], d["Part"]) in in_lib:
        continue
    if d["Kind"] == "email":
        continue
    n += 1
    if n <= 30:
        print(f"  {r.get('Year','')} | {r.get('Job') or '-'} | {d['Kind']} | {d['Words']:>6} w | topics={len([t for t in d['Topics'].split('|') if t])} | {os.path.basename(d['RelPath'])[:70]}")
print("  total:", n)

print("=== documents in the library with role executed ===")
seen = set()
for r in lib:
    if r["Role"] == "executed" and (r["File"], r["Part"]) not in seen:
        seen.add((r["File"], r["Part"]))
        print(f"  {r['Date'][:4]} | {r['Job']} | {r['ClientName'][:22]} | {os.path.basename(r['File'])[:60]}{(' > ' + r['Part'][:25]) if r['Part'] else ''}")
print("  total:", len(seen))

print("=== limitation_of_liability clauses, latest on file or executed ===")
for r in lib:
    if "limitation_of_liability" in r["Topics"].split("|") and (r["Latest"] == "yes" or r["Role"] == "executed"):
        t = re.sub(r"\s+", " ", r["Text"])
        m = re.search(r"(\$\s?[\d,]+(\.\d+)?|two times|2 times|twice|the (fee|price|total)[^.]{0,40})", t, re.I)
        print(f"  {r['Date'][:4]} | {r['Job']} | {r['ClientName'][:20]} | {r['Role']} | {r['Heading'][:38]} | key: {(m.group(0) if m else '-')[:40]}")
        print("      ", t[:220])

print("=== client-paper jobs in the library, by client ===")
jobs = defaultdict(set)
for r in lib:
    if r["Paper"] == "client":
        jobs[r["ClientName"] or "(no Deltek client)"].add(r["Job"])
for c, js in sorted(jobs.items(), key=lambda kv: -len(kv[1]))[:25]:
    print(f"  {len(js)} | {c[:40]} | {' '.join(sorted(js))[:60]}")
print("  clients:", len(jobs), "jobs:", len({j for js in jobs.values() for j in js}))
