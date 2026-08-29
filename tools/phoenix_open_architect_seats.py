#!/usr/bin/env python3
"""Phoenix rezoning cases where the developer is named and the architect is not.

WHY THIS IS THE ONE LIST THAT MATTERS
    Almost every public construction record exists BECAUSE A DESIGN TEAM FILED
    IT. TDLR names the design firm because they submitted it; a site plan was
    drawn by somebody; a UDRB packet is the architect's own drawing set. By the
    time those are public the commission is gone.

    A PUD rezoning narrative is different. It is written for the DEVELOPER, it
    carries a PROJECT TEAM block, and that block has a slot for the architect
    alongside civil, landscape and counsel. When the developer is named, the
    civil engineer is named, the attorney is named AND THE ARCHITECT SLOT IS
    EMPTY, that is not a missing database field. It is a team that has not
    appointed one.

⚠ THE LIMIT, AND IT IS REAL
    Absence in a document is still not proof of absence in the world. A
    developer may have an architect and simply not have listed them. What can
    be said honestly: these are companies actively entitling land in Phoenix
    who have not put an architect on the record. That is a call list, not a
    guarantee, and it must be described that way.

    Compare Charlotte, where NO petition names a design team, so absence there
    carries no signal at all - it is how that city publishes. In Phoenix the
    same narrative names an architect two times in three, which is what makes
    the empty third worth something.

USAGE
    python phoenix_open_architect_seats.py [teams.csv] [from_case_year]
"""
import csv
import os
import re
import sys

# Names that are extraction debris rather than companies. The developer field
# in these narratives runs on from the preceding label often enough to matter.
JUNK = re.compile(r"(?i)^(s |ure |re |\W*$)|^(s within|s vestar|vp lb)")
NOT_ARCH = {"KIMLEYHORN": "civil engineers",
            "FORRESTRICHARDSON": "golf course architect"}


def key(s):
    return re.sub(r"[^A-Za-z0-9]", "", s or "").upper()


def main(path, from_year=25):
    rows = list(csv.DictReader(open(path, encoding="utf-8")))
    out = []
    for r in rows:
        m = re.match(r"z-\d+-(\d\d)", (r.get("case") or ""))
        if not m or int(m.group(1)) < from_year:
            continue
        dev = (r.get("developer") or "").strip()
        arch = (r.get("architect") or "").strip()
        if NOT_ARCH.get(key(arch)):
            arch = ""          # an engineer in the architect slot is not one
        if not dev or JUNK.match(dev):
            continue
        out.append({"case": r["case"], "developer": dev, "architect": arch,
                    "civil": (r.get("civil") or "").strip(),
                    "attorney": (r.get("attorney") or "").strip()})

    seats = [r for r in out if not r["architect"]]
    taken = [r for r in out if r["architect"]]
    print("Phoenix rezoning cases from 20%d, with a developer named : %d"
          % (from_year, len(out)))
    print("   architect named in the team block                    : %d" % len(taken))
    print("   ARCHITECT SLOT EMPTY                                 : %d" % len(seats))
    print()
    print("%-14s %-38s %s" % ("CASE", "DEVELOPER", "ALSO ON THE TEAM"))
    for r in seats:
        others = ", ".join(x for x in (r["civil"], r["attorney"]) if x)[:40]
        print("%-14s %-38s %s" % (r["case"][:14], r["developer"][:38],
                                  others or "-"))
    print()
    print("for contrast, cases where the seat is taken:")
    for r in taken:
        print("   %-14s %-32s %s" % (r["case"][:14], r["developer"][:32],
                                     r["architect"][:32]))


if __name__ == "__main__":
    default = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                           "docs", "audit-2026-08", "mve-pipeline",
                           "phoenix-pud-teams-2026.csv")
    main(sys.argv[1] if len(sys.argv) > 1 else default,
         int(sys.argv[2]) if len(sys.argv) > 2 else 25)
