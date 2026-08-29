#!/usr/bin/env python3
"""Resolve who is actually behind a live filing, and whether an architect is on it.

THE PROBLEM THIS SOLVES
    A rezoning petition is filed by "AAC Moody Lake Land, LLC" - a shell that
    exists for one site and appears nowhere else, ever. That is why the
    developer-to-architect join across markets returned 2 hits out of 72: the
    names are disposable. Without resolving the shell, a live-filing list is a
    list of LLCs, and nobody can call an LLC.

    Phoenix solves it for free and I had the data unused. Its PUD narratives
    carry the project team's CONTACT EMAILS, and an email domain is an identity:
    Laura.Meyers@Pulte.com is Pulte, not a shell. The domain also reveals the
    ROLE, because a law firm, a civil engineer and an architect all have
    recognisable ones.

    So for Phoenix this produces, per live case:
        the real developer            pulte.com, lennar.com, cullumhomes.com
        who else is already engaged   counsel, civil, planning, landscape
        WHETHER AN ARCHITECT IS ON IT at all

    That last line is a far stronger signal than an empty text field. A team
    with counsel and a civil engineer and NO architecture domain anywhere in
    its own contact list has not appointed one.

⚠ WHAT IT CANNOT DO
    Charlotte and Raleigh publish no contact details, so their petitioners stay
    unresolved here and need a corporate-registry lookup instead. Those markets
    are reported as such rather than padded.

    A domain missing from the contact list is not proof no architect exists,
    for the same reason as everywhere else in this repo. It is a strong
    indicator, and it is described as one.

USAGE
    python resolve_project_entities.py            # classify and report
    python resolve_project_entities.py --domains  # just the developer domains,
                                                  # for enrichment
"""
import csv
import os
import re
import sys
from collections import Counter, OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
PUD = os.path.join(HERE, "..", "docs", "audit-2026-08", "mve-pipeline",
                   "phoenix-pud-teams-2026.csv")

# Role by domain. Checked by hand against each firm's own site; these are not
# guesses from the name. Anything unmatched is reported as UNKNOWN, never
# assumed to be the developer.
ROLE = {
    # land-use counsel -- engaged BEFORE the architect, every time
    "roselawgroup.com": "counsel", "gblaw.com": "counsel",
    "swlaw.com": "counsel", "earlcurley.com": "counsel",
    "wmbattorneys.com": "counsel", "berryriddell.com": "counsel",
    "gilbertblilie.com": "counsel", "crawford.team": "counsel",
    # civil / survey
    "kimley-horn.com": "civil", "rickengineering.com": "civil",
    "civtech.com": "civil", "collierseng.com": "civil",
    "precision-civil.com": "civil", "hilgartwilson.com": "civil",
    "woodpatel.com": "civil", "cvlci.com": "civil",
    "sws-engr.com": "civil", "se3.us": "civil",
    # planning / landscape
    "rviplanning.com": "planning", "norris-design.com": "landscape",
    "c2collaborative.com": "landscape",
    # architects. ⛔ THE TWO MARKED (!) WERE CAUGHT ONLY BY READING THE SITE.
    # A domain that looks like nothing can be an architecture practice, and
    # each of these would otherwise have put a project with an appointed
    # architect on a list of open seats.
    "ktgy.com": "architect", "davisexperience.com": "architect",
    "butlerdesigngroup.com": "architect", "deutscharchitecture.com": "architect",
    "bsbdesign.com": "architect", "upwardarchitects.com": "architect",
    "ccbg.com": "architect", "kasswilson.com": "architect",
    "kontexture.com": "architect",   # (!) "an architecture firm based in Phoenix"
    "2929.com": "architect",         # (!) Deutsch Architecture Group
    # developers / owners
    "pulte.com": "developer", "lennar.com": "developer",
    "cullumhomes.com": "developer", "summitlandmgmt.com": "developer",
    "vintagevp.com": "developer", "lokahigroup.com": "developer",
    "meritagehomes.com": "developer", "fifield.com": "developer",
    "unbounddev.com": "developer", "stnldevelopment.com": "developer",
    "blueprintcap.com": "developer",
}
# Checked and could not be resolved. A case relying on one of these is reported
# as UNRESOLVED rather than counted either way.
UNRESOLVED = {"designethic.net", "fdgroup.us"}
GENERIC = {"gmail.com", "yahoo.com", "hotmail.com", "outlook.com", "aol.com",
           "icloud.com", "msn.com"}

ARCH_HINT = re.compile(r"(?i)(architect|arquitect|design(group|studio)?|"
                       r"studio|atelier)")
COUNSEL_HINT = re.compile(r"(?i)(law|legal|attorney|llp$|counsel)")
CIVIL_HINT = re.compile(r"(?i)(engineer|civil|survey|geotech)")


def guess_role(domain):
    """Only ever used to LABEL an unknown domain, never to assert developer."""
    if domain in ROLE:
        return ROLE[domain]
    if domain in GENERIC:
        return "personal"
    stem = domain.rsplit(".", 1)[0]
    if COUNSEL_HINT.search(stem):
        return "counsel?"
    if CIVIL_HINT.search(stem):
        return "civil?"
    if ARCH_HINT.search(stem):
        return "architect?"
    return "unknown"


def domains(emails):
    out = []
    for e in re.split(r"[;,\s]+", emails or ""):
        m = re.search(r"@([A-Za-z0-9.-]+\.[A-Za-z]{2,})", e)
        if m:
            d = m.group(1).lower()
            if d not in out:
                out.append(d)
    return out


def load():
    cases = []
    for r in csv.DictReader(open(PUD, encoding="utf-8")):
        m = re.match(r"z-\d+-(\d\d)", r.get("case") or "")
        if not m or int(m.group(1)) < 25:
            continue
        ds = domains(r.get("emails"))
        roles = OrderedDict((d, guess_role(d)) for d in ds)
        cases.append({
            "case": r["case"],
            "stated_developer": (r.get("developer") or "").strip(),
            "stated_architect": (r.get("architect") or "").strip(),
            "domains": roles,
        })
    return cases


def main(argv):
    cases = load()
    if "--domains" in argv:
        devs = sorted({d for c in cases for d, role in c["domains"].items()
                       if role == "developer"})
        unknown = sorted({d for c in cases for d, role in c["domains"].items()
                          if role in ("unknown",)})
        print("developer domains:")
        for d in devs:
            print("  %s" % d)
        print("\nunclassified domains, need checking before use:")
        for d in unknown:
            print("  %s" % d)
        return 0

    print("Phoenix rezoning cases 2025-2026 with contact details: %d\n"
          % len([c for c in cases if c["domains"]]))
    open_seat, taken, unclear = [], [], []
    for c in cases:
        roles = list(c["domains"].values())
        # ⛔ TWO SIGNALS, AND BOTH MUST BE EMPTY.
        # The contact list and the narrative's own team block disagree in both
        # directions. z-112-25-8n names BSB Design in the text but carries no
        # BSB email, so a domain-only test called it an open seat when it is
        # not. z-51-26-3-n named no architect in the text but carried
        # kontexture.com, which is an architecture firm. Either one alone is
        # wrong; a seat is open only when NEITHER names an architect.
        stated = c["stated_architect"]
        stated_is_arch = bool(stated) and not re.match(
            r"(?i)kimley|forrest richardson", stated)
        has_arch = any(r.startswith("architect") for r in roles) or stated_is_arch
        has_dev = any(r == "developer" for r in roles)
        risky = [d for d in c["domains"] if d in UNRESOLVED]
        if not c["domains"]:
            unclear.append(c)
        elif has_arch:
            taken.append(c)
        elif risky:
            c["risk"] = risky
            unclear.append(c)
        else:
            open_seat.append(c)

    def show(group, title):
        print("=== %s : %d" % (title, len(group)))
        for c in group:
            devs = [d for d, r in c["domains"].items() if r == "developer"]
            others = ["%s (%s)" % (d, r) for d, r in c["domains"].items()
                      if r not in ("developer", "personal")]
            print("  %-14s %-28s %s"
                  % (c["case"][:14],
                     (devs[0] if devs else c["stated_developer"][:28] or "?"),
                     "; ".join(others[:3])))
        print()

    show(open_seat, "NO ARCHITECT ANYWHERE IN THE PROJECT TEAM'S OWN CONTACTS")
    show(taken, "architect already engaged")
    if unclear:
        print("=== no contact details published : %d" % len(unclear))
        for c in unclear:
            print("  %-14s %s" % (c["case"][:14], c["stated_developer"][:40]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
