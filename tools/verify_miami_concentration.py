#!/usr/bin/env python3
"""Check the Miami figures, and be exact about which of them this can check.

⛔ THE TRAP THAT NEARLY BROKE A CORRECT DOCUMENT
    Counting the rows in udrb_teams.py's output gives 47 named across 16 firms,
    with Kobi Karp on 8 and Arquitectonica on 7. The companion prints 35 across
    13, with those two TIED ON SIX. It looks like a stale figure and it is not:

        the companion counts DISTINCT PROJECTS.
        the TSV rows are BOARD APPEARANCES.

    The board defers and re-hears items, so a project heard twice is two rows
    and one project. The companion says so in its own limits paragraph -- "ten
    of its cases have been heard at more than one meeting, so counting
    appearances would credit a firm twice for one project". The 47 is the number
    that paragraph exists to warn against.

    This is the second time in this work that a "correction" would have made a
    right document wrong; the Raleigh 42% was the first. READ THE LIMITS
    PARAGRAPH BEFORE CORRECTING A NUMBER.

⛔ AND THE SECOND TRAP, HIT IMMEDIATELY AFTER THE FIRST
    Counting PZ anchors in the PACKETS gives 33, against a printed 66, which
    looks like another error. It is not. The 66 comes from the AGENDAS, which
    cover every meeting; the packets are a subset (27 downloaded, one of them
    failing pdftotext) and their anchor wording varies enough that the regex
    under-captures on them. Run against udrb_agendas/ the count is exact:
    78 project items, 66 distinct PZ cases.

    Twice in five minutes the "error" was the measurement, not the document.
    MATCH THE POPULATION BEFORE COMPARING A NUMBER.

⚠ WHAT THIS CAN AND CANNOT VERIFY
    CAN:    the number of distinct projects heard, from the AGENDAS -- which is
            the "66 projects" figure that ships in the client document.
    CANNOT: the per-project architect counts. udrb_teams.py emits
            (packet, item, firm) and never links an item to its PZ case number,
            so the TSV cannot be deduped to projects. Reproducing 35/13/17%
            needs that mapping added to the tool first.

    Saying which is which is the point. A verifier that quietly checks the easy
    half and reports PASS is worse than no verifier.

USAGE
    python verify_miami_concentration.py <agenda-dir> [teams.tsv]
"""
import csv
import glob
import io
import os
import re
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

from tabs_projects import norm_firm  # noqa: E402
# ⛔ Use the agenda parser itself. Reimplementing the case-number regex here
#    produced 28 against a true 66: udrb_agendas.parse() handles six wording
#    variants that udrb_teams' packet-tuned ANCHOR does not.
from udrb_agendas import parse as parse_agenda  # noqa: E402

PRINTED_PROJECTS_HEARD = 66
NOT_A_FIRM = re.compile(
    r"(?i)^(anthony leon|brian vargo)|describe a dock|ocean consulting")


def main(agenda_dir, tsv=None):
    # ⛔ AGENDAS, not packets. The packets are a subset of meetings and their
    #    anchor wording varies enough that the regex under-captures on them --
    #    33 against a true 66. The agendas are small, complete, and exact.
    files = sorted(glob.glob(os.path.join(agenda_dir, "*.txt")))
    print("agendas on disk: %d" % len(files))

    cases, items = set(), 0
    for p in files:
        for row in parse_agenda(p):
            pz = (row.get("pz") if isinstance(row, dict) else row[0]) or ""
            pz = re.sub(r"[\s\-]", "", str(pz)).upper()
            if pz:
                cases.add(pz)
                items += 1
    print("project items across the agendas: %d" % items)

    print()
    print("VERIFIABLE HERE")
    ok = len(cases) == PRINTED_PROJECTS_HEARD
    print("   distinct PZ cases heard    printed %-4d actual %-4d %s"
          % (PRINTED_PROJECTS_HEARD, len(cases), "ok" if ok else "MISMATCH"))

    if tsv and os.path.exists(tsv):
        rows = list(csv.DictReader(open(tsv, encoding="utf-8"), delimiter="\t"))
        named = [r["architect"].strip() for r in rows
                 if r.get("architect", "").strip()
                 and not NOT_A_FIRM.search(r["architect"])]
        firms = Counter(norm_firm(x) for x in named)
        print()
        print("NOT VERIFIABLE HERE -- appearance counts, NOT project counts")
        print("   board appearances naming an architect : %d" % len(named))
        print("   distinct firms across appearances     : %d" % len(firms))
        print("   ⚠ the companion's 35 / 13 / 17%% are per PROJECT. These are")
        print("     per APPEARANCE and are NOT the same measure. Do not treat")
        print("     a difference between them as an error in the document.")

    print()
    print("TO CLOSE THE GAP")
    print("   udrb_teams.py must carry the PZ case number onto each item row.")
    print("   Until it does, 35 / 13 / 17%% rest on the original run and cannot")
    print("   be reproduced from what is on disk.")
    print()
    if not ok:
        print("FAIL -- the projects-heard count does not reproduce")
        return 1
    print("PARTIAL PASS -- projects-heard reproduces; per-project shares "
          "unverified by design")
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None))
