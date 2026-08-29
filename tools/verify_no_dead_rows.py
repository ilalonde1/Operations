#!/usr/bin/env python3
"""Refuse to ship a lead table containing work that is already committed.

⛔ WHY THIS IS A BUILD GATE AND NOT A HABIT
    A row reading "9 Dec 2025 | Sierra Verde Townhomes | Final site plan"
    survived THREE separate rounds of review. Each time it was noticed by the
    client, not by us, and each time it was fixed by hand and came back. A
    final site plan is a committed scheme: the design is done, the architect was
    engaged a year earlier, and nobody reading it can act on it.

    Anything caught by eye three times belongs in the build.

WHAT IT REFUSES
  * stage words that mean the design is settled -- "final site plan",
    "issued", "completed", "under construction", "topped out", "opened"
  * a date older than STALE_MONTHS sitting in a row of a table, unless the row
    is explicitly marked as historical context

⚠ THIS IS ABOUT TABLES, NOT PROSE
    "Final EIS accepted" is a legitimate and important thing to say about
    Makena Mauka -- an accepted EIS means entitlement is FINISHING and the
    building work is next, which is the opposite of committed design. So the
    stage words are only refused where they describe a filing stage in a
    project row. ALLOW carries those exceptions explicitly.

USAGE
    python verify_no_dead_rows.py [pdf]
"""
import io
import os
import re
import sys

import pypdf

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT = os.path.join(REPO, "docs",
                       "KOR-MVE-Six-Market-Record-2026-08-28-web.pdf")

# Phrases that mean the design conversation is over.
COMMITTED = [
    "final site plan",
    "permit issued",
    "under construction",
    "topped out",
    "certificate of occupancy",
    "ribbon-cutting",
    "grand opening",
]

# Legitimate uses of the same words. Each must be a real, checked exception.
ALLOW = [
    # An accepted Final EIS means entitlement is finishing -- design comes NEXT.
    "final eis",
    "final eis accepted",
    # We describe the trap itself in the exclusions/《caveat》 wording.
    "a final site plan is a committed scheme",
    "final one is a scheme committed",
    "42 are final",
    "are preliminary and",
    # The "TO BE BID" disproof quotes the final-site-plan count as evidence:
    # "appears on 59 of the 92 final site plans". That sentence is the reason
    # the field is NOT used, so it must not trip the gate.
    "59 of the 92 final site plans",
    "92 final",
    "281 preliminary and 92 final",
]

STALE_MONTHS = 9
STALE = re.compile(r"(?i)\b(\d{1,2}\s+)?(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)"
                   r"[a-z]*\s+(20(?:1\d|2[0-4]))\b")

# ⚠ AN OLD DATE IS NOT AUTOMATICALLY A DEAD ROW.
#    The first version of this gate failed the document on four dates that were
#    all doing legitimate work:
#      * "OLD FEED STOPPED: Feb 2021 / Mar 2020 / May 2022" -- a table whose
#        entire point is that those feeds are dead and were replaced by a live
#        one. The staleness IS the finding.
#      * "66 projects ... since Jan 2024" -- the start of a measurement window.
#    A gate that cries wolf gets switched off, so it has to know the difference.
#    Flag a stale date only when nothing nearby explains why it is old.
STALE_OK = re.compile(
    r"(?i)(stopped|old feed|since|founded|established|acquired|developed|"
    r"through|between|from|window|history|historic|prior|previous|"
    r"replaced|migrated|dead)")


def main(path):
    r = pypdf.PdfReader(path)
    text = re.sub(r"\s+", " ", "\n".join((p.extract_text() or "")
                                         for p in r.pages))
    low = text.lower()
    print(os.path.basename(path))
    print("  %d pages" % len(r.pages))
    print()

    fails = []

    print("COMMITTED-STAGE WORDING")
    for phrase in COMMITTED:
        hits = [m.start() for m in re.finditer(re.escape(phrase), low)]
        clean = []
        for h in hits:
            seg = low[max(0, h - 120):h + 120]
            if any(a in seg for a in ALLOW):
                continue
            clean.append(seg)
        status = "ok" if not clean else "PRESENT x%d" % len(clean)
        print("   %-28s %s" % (phrase, status))
        for seg in clean[:2]:
            print("        ...%s..." % seg.strip()[:110])
            fails.append("committed-stage wording in a row: %r" % phrase)

    print()
    print("STALE DATES (older than %d months, i.e. 2024 or earlier here)"
          % STALE_MONTHS)
    unexplained, explained = [], []
    for m in STALE.finditer(text):
        seg = text[max(0, m.start() - 150):m.start() + 90]
        (explained if STALE_OK.search(seg) else unexplained).append(
            (m.group(0).strip(), seg))
    if explained:
        print("   %d explained (old-feed / measurement window): %s"
              % (len(explained),
                 ", ".join(sorted({d for d, _ in explained}))[:80]))
    if unexplained:
        for d, seg in unexplained[:4]:
            print("   UNEXPLAINED %-12s ...%s..." % (d, seg.strip()[-110:]))
            fails.append("stale date with no context explaining it: %s" % d)
    else:
        print("   none unexplained")

    print()
    if fails:
        print("FAIL -- %d problem(s):" % len(fails))
        for f in dict.fromkeys(fails):
            print("   * %s" % f)
        return 1
    print("PASS -- no committed or stale rows in the shipped PDF")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT))
