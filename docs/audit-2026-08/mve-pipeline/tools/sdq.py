"""Query the San Diego DSD approvals CSV. Prints the fields that matter for stage.

Usage: python sdq.py <regex> [max_rows]
Matches against PROJECT_TITLE + PROJECT_SCOPE + GIS_ADDRESS.
"""
import csv, re, sys

pat = re.compile(sys.argv[1], re.I)
limit = int(sys.argv[2]) if len(sys.argv) > 2 else 40

KEEP = ["PROJECT_ID", "PROJECT_STATUS", "PROJECT_CREATE_DATE", "GIS_ADDRESS",
        "APPROVAL_ID", "APPROVAL_TYPE", "APPROVAL_STATUS",
        "APPROVAL_CREATE_DATE", "APPROVAL_ISSUE_DATE",
        "APPROVAL_VALUATION", "APPROVAL_STORIES", "APPROVAL_FLOOR_AREA",
        "APPROVAL_PERMIT_HOLDER"]

csv.field_size_limit(10 ** 7)
n = 0
seen = set()
with open("sd_active.csv", encoding="utf-8", errors="replace", newline="") as f:
    for row in csv.DictReader(f):
        hay = " ".join([row.get("PROJECT_TITLE") or "", row.get("PROJECT_SCOPE") or "",
                        row.get("GIS_ADDRESS") or "", row.get("APPROVAL_SCOPE") or ""])
        if not pat.search(hay):
            continue
        key = (row.get("APPROVAL_ID"), row.get("PROJECT_ID"))
        if key in seen:
            continue
        seen.add(key)
        vals = " | ".join(f"{k.replace('APPROVAL_', 'A_').replace('PROJECT_', 'P_')}={row.get(k)}"
                          for k in KEEP if row.get(k))
        print(vals)
        scope = (row.get("PROJECT_SCOPE") or "")[:190].replace("\n", " ")
        print("    scope:", scope)
        n += 1
        if n >= limit:
            break
print("--- matched", n, "rows")
