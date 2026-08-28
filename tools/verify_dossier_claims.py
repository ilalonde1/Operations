"""Re-derive every number the dossier states, from the data, and compare.

Not a spell-check of the PDF: each claim is recomputed from the source file and
asserted against what is printed. A document that contradicts itself between
page 4 and page 10 is the worst failure available here, and nothing so far has
tested for it.
"""
import json
import re
import subprocess
import sys
from collections import Counter

sys.path.insert(0, r"C:\VIsual Studio Projects\Operations\tools")
from tabs_projects import norm_firm, sector_of  # noqa: E402

PDF = r"C:\VIsual Studio Projects\Operations\docs\KOR-MVE-Design-Team-Dossier-2026-08-28-web.pdf"
text = subprocess.run(["pdftotext", "-q", PDF, "-"], capture_output=True,
                      text=True, encoding="utf-8", errors="replace").stdout
flat = re.sub(r"\s+", " ", text)

rows = [json.loads(l) for l in open("harris_detail.jsonl", encoding="utf-8")]
idx = [json.loads(l) for l in open("harris.jsonl", encoding="utf-8")]

checks = []


def claim(label, printed, actual):
    ok = str(printed) == str(actual)
    checks.append((ok, label, printed, actual))


# --- Houston, recomputed from the census -------------------------------------
named = [r for r in rows if (r.get("Design Firm Name") or "").strip()]
firms_all = Counter(norm_firm(r["Design Firm Name"]) for r in named)
claim("Houston new-construction projects", 4087, len(rows))
claim("Houston with a named design firm", 3085, len(named))
claim("Houston distinct firms", 806, len(firms_all))
claim("Houston top firm share %", 3,
      round(100 * firms_all.most_common(1)[0][1] / sum(firms_all.values())))

mf = [r for r in rows if sector_of(r) == "multifamily"
      and (r.get("Design Firm Name") or "").strip()]
mff = Counter(norm_firm(r["Design Firm Name"]) for r in mf)
claim("Houston multifamily projects", 79, len(mf))
claim("Houston multifamily firms", 45, len(mff))
claim("Houston multifamily top share %", 10,
      round(100 * mff.most_common(1)[0][1] / sum(mff.values())))

new_since_june = [r for r in idx if r.get("TypeOfWork") == 9001
                  and r["ProjectCreatedOn"][:7] >= "2026-06"]
claim("Houston new construction since 1 June 2026", 390, len(new_since_june))
claim("Houston most recent record", "2026-08-27",
      max(r["ProjectCreatedOn"] for r in idx)[:10])

# --- Multifamily repeat owners ------------------------------------------------
SPV = re.compile(r"(?i)[,\.]?\s*\b(llc|lp|ltd|inc|corp|corporation|company|co|"
                 r"holdings?|properties|property|partners(hip)?|investments?|"
                 r"capital|realty|group|trust|associates|ventures?|"
                 r"developments?)\b\.?")


def norm_owner(n):
    core = re.sub(r"\s+", " ", (n or "")).strip()
    for _ in range(6):
        new = SPV.sub(" ", core)
        if new == core:
            break
        core = new
    return re.sub(r"\s+", "", re.sub(r"[^A-Za-z0-9& ]", " ", core)).upper()


og = Counter(norm_owner(r.get("Owner Name") or "") for r in mf)
# Two DIFFERENT quantities, and conflating them is what put a false number in
# the document: 7 owners built more than once; those 7 hold 12 projects; the
# remaining 67 of 79 each have an owner that appears once.
claim("Houston multifamily OWNERS building more than once", 7,
      len([k for k, v in og.items() if v >= 2 and k]))
claim("Houston multifamily PROJECTS held by a repeat owner", 12,
      sum(v for k, v in og.items() if v >= 2 and k))
claim("Houston multifamily projects with a one-time owner", 67,
      len(mf) - sum(v for k, v in og.items() if v >= 2 and k))

print("=== claims re-derived from the data ===")
for ok, label, printed, actual in checks:
    print("  %s %-48s printed %-12s actual %s"
          % ("ok  " if ok else "!!!!", label, printed, actual))

print("\n=== numbers that appear in the PDF more than once (must agree) ===")
for pat, why in ((r"\b4,?087\b", "Houston project count"),
                 (r"\b806\b", "Houston distinct firms"),
                 (r"\b79\b", "Houston multifamily projects"),
                 (r"\b45\b", "Houston multifamily firms"),
                 (r"\b21\b", "Miami projects"),
                 (r"\b12\b", "Arizona projects"),
                 (r"\b11\b", "Raleigh projects / Arizona firms"),
                 (r"36%", "Raleigh top share"),
                 (r"17%", "Arizona top share")):
    n = len(re.findall(pat, flat))
    print("  %-42s appears %2d time(s)" % (why, n))
