#!/usr/bin/env python3
"""Re-derive the Raleigh headline from the extracted plan-set records.

WHY THIS EXISTS
    "Raleigh has an incumbent, and it just changed hands" is the second half of
    the dossier's cover claim and the control that makes the Arizona finding
    mean anything: a no-incumbent result is only interesting if the same
    measurement can produce the opposite answer somewhere else.

    Architects are not in Raleigh's permit feed and not on the site-review
    application form -- that form carries owner and applicant, and the applicant
    is normally the civil engineer, which is exactly how Bass, Nixon & Kennedy
    (consulting engineers) once got counted as an architect. The firm is on the
    DRAWING SET, in the sheet copyright block:
        "(C) 2024 JDAVIS ARCHITECTS EXPRESSLY RESERVES ITS COMMON LAW COPYRIGHT"
    which repeats on every sheet and names the practice unambiguously.

    Coverage: 92 multifamily plans submitted since January 2024; most are
    published as the form only, with no drawings attached. The percentages are
    computed over the sets that can answer, and this script states how many
    those are rather than leaving it implied.

USAGE
    python verify_raleigh_claim.py [records.json]
    Defaults to docs/audit-2026-08/mve-pipeline/raleigh-plansets-2026.json
"""
import json
import os
import re
import sys
from collections import Counter

DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                       "docs", "audit-2026-08", "mve-pipeline",
                       "raleigh-plansets-2026.json")

PRINTED = {
    "projects with a confirmed architect": 11,
    "distinct architecture firms": 7,
    "top firm projects": 4,
    "top firm share %": 36,
    "firms per project": "0.64",
}

# ⛔ COUNT PROJECTS, NOT PLAN RECORDS. The population is 92 records: 61 ASR
# (site-plan review) and 31 SUB (subdivision), and ONE SCHEME CAN APPEAR AS
# BOTH. "DSLC - Mitchell Mill Apartments" is filed as ASR-0015-2024 and again
# as SUB-0027-2024. Counting records instead of projects credits JDAVIS with
# five projects and a 42% share when it holds four and 36% -- an error this
# script made, and nearly wrote into the document, before the project NAMES
# were merged in and the duplicate became visible. Deduplicate on the name.

# Checked, and NOT architects. Each was actually captured as one before being
# screened out; see the verification log.
NOT_ARCHITECT = {
    "KIMLEYHORNANDASSOCIATES": "civil engineers, taken from the Applicant field",
    "KIMLEYHORN": "civil engineers",
    "BASSNIXONKENNEDYINC": "consulting engineers (civil/MEP/survey), from the "
                           "Applicant field",
    "BASSNIXONKENNEDY": "consulting engineers (civil/MEP/survey)",
    "SOUTHEASTERNARCHITECTURALSYSTEMS": "a screen-system manufacturer in a "
                                        "materials schedule",
}
# General-notes text that a bare ARCHITECT label drags in off a drawing sheet.
NOTES = re.compile(r"(?i)^(prior to|of any|owner$|contractor|the |all |any |"
                   r"see |refer |verify|provide|install|note)")


def key(name):
    """Collapse spelling variants -- the same practice files as both
    "CLINE DESIGN ASSOCIATES" and "CLINE DESIGN ASSOCIATES, PA"."""
    n = re.sub(r"(?i)[,.]?\s*\b(inc|llc|pllc|pa|ltd|lp|pc)\b\.?", " ",
               name or "")
    return re.sub(r"[^A-Za-z0-9]", "", n).upper()


def main(path):
    recs = json.load(open(path, encoding="utf-8"))
    print("source: %s" % os.path.normpath(path))
    print("plan records in the set: %d" % len(recs))

    with_arch = [r for r in recs if (r.get("architect") or "").strip()]
    kept, dropped = [], []
    for r in with_arch:
        a = r["architect"].strip()
        k = key(a)
        why = NOT_ARCHITECT.get(k)
        if why:
            dropped.append((a, why))
        elif NOTES.match(a):
            dropped.append((a, "drawing general-notes text, not a firm"))
        else:
            kept.append(r)

    # One project, one entry -- collapse a scheme filed under both an ASR and a
    # SUB number. Fall back to the plan number when no name was recorded.
    by_project = {}
    for r in kept:
        pid = (r.get("project") or "").strip().upper() or r["plan"]
        by_project.setdefault(pid, r)
    merged = len(kept) - len(by_project)
    if merged:
        print("  collapsed %d duplicate plan record(s) onto their project:"
              % merged)
        seen = {}
        for r in kept:
            pid = (r.get("project") or "").strip().upper() or r["plan"]
            seen.setdefault(pid, []).append(r["plan"])
        for pid, plans in seen.items():
            if len(plans) > 1:
                print("      %-40s %s" % (pid[:40], ", ".join(plans)))
        print()

    kept = list(by_project.values())
    firms = Counter(key(r["architect"]) for r in kept)
    display = {}
    for r in kept:
        display.setdefault(key(r["architect"]), r["architect"].strip())
    top_key, top_n = firms.most_common(1)[0]

    actual = {
        "projects with a confirmed architect": len(kept),
        "distinct architecture firms": len(firms),
        "top firm projects": top_n,
        "top firm share %": round(100 * top_n / len(kept)),
        "firms per project": "%.2f" % (len(firms) / len(kept)),
    }

    print("  plan records naming something in the architect field: %d"
          % len(with_arch))
    print("  screened out as not an architect: %d" % len(dropped))
    for a, why in dropped:
        print("      %-46s %s" % (a[:46], why))
    print()
    ok = True
    for k2, printed in PRINTED.items():
        good = str(printed) == str(actual[k2])
        ok &= good
        print("  %s %-38s printed %-6s actual %s"
              % ("ok  " if good else "!!!!", k2, printed, actual[k2]))
    print("\n  every firm and its count:")
    for k2, c in firms.most_common():
        print("      %-46s %d" % (display[k2][:46], c))
    print("\nRESULT: %s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT))
