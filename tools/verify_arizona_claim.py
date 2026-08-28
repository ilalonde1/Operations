#!/usr/bin/env python3
"""Re-derive the Arizona headline from the parsed source rows and assert it.

WHY THIS EXISTS
    "Arizona has no incumbent architect" is the COVER CLAIM of the MVE dossier,
    and it is the only headline in that document whose evidence is a trade
    publication rather than a public record: AZ Big Media's "50 commercial real
    estate projects to know in 2026", which names a design team on 49 of 50.

    Every other figure in the dossier can be recomputed from data held in this
    repository. This one could not -- the parsed rows lived only in a session
    scratchpad, so the cover claim was the least auditable thing in the report.
    The rows are now committed beside this script and the numbers are asserted,
    not remembered.

    The finding was separately corroborated against Phoenix PUD rezoning
    narratives -- an unrelated source that cannot have been shaped by the
    magazine's editing -- which returned the same dispersion. See
    reference_entitlement_and_permit_research, and tools/phx_pud_teams.py.

USAGE
    python verify_arizona_claim.py [rows.json]
    Defaults to docs/audit-2026-08/mve-pipeline/az-50-projects-2026.json
"""
import json
import os
import re
import sys
from collections import Counter

DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                       "docs", "audit-2026-08", "mve-pipeline",
                       "az-50-projects-2026.json")

# The claims exactly as the dossier prints them.
PRINTED = {
    "rows in the set": 50,
    "multifamily rows in the set": 12,
    "projects naming an architect": 11,
    "architect credits (a shared project counts twice)": 12,
    "distinct architecture firms": 11,
    "top firm projects": 2,
    "top firm share of projects %": 18,
    "multifamily share of the set %": 24,
}


def norm(name):
    n = re.sub(r"(?i)[,\.]?\s*\b(inc|llc|ltd|lp|pc|corp|corporation|company|"
               r"architects?|architecture|and associates|& associates|"
               r"associates|group|studio|design|designs)\b\.?", " ", name or "")
    return re.sub(r"[^A-Za-z0-9&]", "", n).upper()


def main(path):
    rows = json.load(open(path, encoding="utf-8"))
    mf = [r for r in rows
          if re.search(r"(?i)multi[- ]?family|multifamily", r.get("sector") or "")]
    named = [r for r in mf if (r.get("arch") or "").strip()]
    # One row reads "Niles Bolton Associates, Gensler" -- two firms on one
    # project. Treating that string as a single firm hid Gensler's second
    # project and put the top-firm share out by a point.
    assigns = [f.strip() for r in named
               for f in r["arch"].split(",") if f.strip()]
    firms = Counter(norm(f) for f in assigns)
    display = {}
    for f in assigns:
        display.setdefault(norm(f), f)
    top_key, top_n = firms.most_common(1)[0]

    actual = {
        "rows in the set": len(rows),
        "multifamily rows in the set": len(mf),
        "projects naming an architect": len(named),
        "architect credits (a shared project counts twice)": len(assigns),
        "distinct architecture firms": len(firms),
        "top firm projects": top_n,
        "top firm share of projects %": round(100 * top_n / len(named)),
        "multifamily share of the set %": round(100 * len(mf) / len(rows)),
    }

    print("source: %s" % os.path.normpath(path))
    ok = True
    for k, printed in PRINTED.items():
        good = printed == actual[k]
        ok &= good
        print("  %s %-32s printed %-5s actual %s"
              % ("ok  " if good else "!!!!", k, printed, actual[k]))
    print("\n  largest holder: %s with %d" % (display[top_key], top_n))
    print("  every firm and its count:")
    for k, c in firms.most_common():
        print("      %-42s %d" % (display[k][:42], c))
    print("\nRESULT: %s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT))
