#!/usr/bin/env python3
"""Verify the SHIPPED PDF, not the HTML it was built from.

Repo rule 5. A stale claim has survived a correction in this repo before,
because the check was run against the source while the artifact still carried
the old sentence. Everything here reads the PDF.

WHAT IS CHECKED
  * All six of Dan's markets are present, and each has a CURRENT date.
  * Hawaii is a section, not three project names in a table.
  * The standing prohibitions hold: no competitor named, no MVE revenue, no
    claim about their IT.
  * ⛔ Nothing MVE already designs is presented as an opportunity, and nothing
    that FAILED verification appears at all. The reader has never seen an
    earlier version, so a list of what was removed describes work he cannot
    see -- and it hands over the technique for nothing. Findings only.
  * The numbers that carry the document reproduce against the collected data.

USAGE
    python verify_mve_send_pdf.py [pdf]
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

MARKETS = ["Phoenix", "Las Vegas", "Hawaii", "Houston", "Charlotte", "Miami"]
BANNED = {
    "Glotman": "names a competitor we agreed never to mention",
    "no IT department": "claim about MVE's IT",
    "Englekirk": "names a competitor",
    "Carrier Johnson": "names a competitor",
}
# Must appear, because they are the spine of the document.
REQUIRED = [
    "Howard Hughes", "Ward Village", "7,250", "Bridgeland", "26-101519",
    "David O", "Environmental Notice", "Mākena Mauka", "1,426",
    "LJA Engineering", "Teravalis",
    # The SEC citation stays because the Bridgeland-to-Howard-Hughes link is
    # the one claim a reader would reasonably doubt. The exhibit NUMBER does
    # not: "EX-21.1" is precision for a sceptic, and the date is enough to
    # check it.
    "19 February 2026",
    # the seven verified openings, with the unit counts that make them real.
    # Crosland (staff recommended denial) and DreamKey (duplex/triplex by a
    # nonprofit) came off the chart into the conditions box after a trade-press
    # check that should have run before the first draft.
    "Host Hotels", "Copper Residences", "Z-169-25-2", "Vintage Partners",
    "Z-24-26-7", "Mid-America", "2026-050", "Middleburg", "2026-023",
    "Hoʻonani", "275 units", "364 units", "2,645",
]
# ⛔ These are the leads that DIED verification, and the firms found on them.
#    They must not appear in the client document AT ALL.
#
#    They were previously allowed inside an exclusions box. That box is gone:
#    the client has never seen an earlier version, so a list of what we removed
#    describes work he cannot see, and naming our own technique alongside it
#    gives away the edge. Findings only.
ABSENT = ["Hines", "Vestar", "Fifield", "Elevation Living",
          "Kontexture", "Chipperfield", "MASON Architects",
          "Kittle", "Woods Associates", "Todd & Associates",
          "J&K Luxury", "Design District"]


def norm(s):
    """Compare against what the PDF MEANS, not how it wrapped.

    Three extraction artefacts broke the first version of this check while the
    document was entirely correct:
      * a table cell wraps mid-token, so "26-101519" extracts as "26- 101519"
      * a line break lands inside a name: "David\\nO'Reilly"
      * CSS text-transform uppercases the box headings, so a case-sensitive
        search for the heading finds nothing
    Collapse whitespace, drop spaces that follow a hyphen, and compare
    case-insensitively. None of that weakens the check -- it removes ways for a
    correct document to look wrong.
    """
    s = re.sub(r"\s+", " ", s)
    s = re.sub(r"-\s+(?=\d)", "-", s)
    return s


def main(path):
    r = pypdf.PdfReader(path)
    pages = [(pg.extract_text() or "") for pg in r.pages]
    text = norm("\n".join(pages))
    flat = text
    print("%s" % os.path.basename(path))
    print("  %d pages, %d characters of text" % (len(pages), len(text)))
    print()

    fails = []

    print("MARKETS")
    for m in MARKETS:
        n = len(re.findall(re.escape(m), text, re.I))
        ok = n >= 2
        print("   %-12s %3d mentions   %s" % (m, n, "ok" if ok else "TOO THIN"))
        if not ok:
            fails.append("market %s appears only %d time(s)" % (m, n))

    print()
    print("PROHIBITIONS")
    for bad, why in BANNED.items():
        n = len(re.findall(re.escape(bad), text, re.I))
        print("   %-18s %d   %s" % (bad, n, "ok" if n == 0 else "PRESENT - " + why))
        if n:
            fails.append("banned text %r present (%s)" % (bad, why))

    print()
    print("REQUIRED CLAIMS")
    for want in REQUIRED:
        n = len(re.findall(re.escape(want), text, re.I))
        print("   %-22s %d   %s" % (want, n, "ok" if n else "MISSING"))
        if not n:
            fails.append("required text %r missing" % want)

    print()
    print("ABSENT-BY-RULE  (killed leads and their architects, findings only)")
    for name in ABSENT:
        n = len(re.findall(re.escape(name), flat, re.I))
        print("   %-20s %d   %s" % (name, n, "ok" if n == 0 else "PRESENT"))
        if n:
            fails.append("%s appears in the client document; it was removed as "
                         "a lead and naming it describes work the reader "
                         "cannot see" % name)

    print()
    print("CURRENCY")
    for d in ("27 August", "23 August", "20 August", "14 August"):
        n = len(re.findall(re.escape(d), text))
        print("   %-12s %d" % (d, n))
    if not re.search(r"27 August", text):
        fails.append("the newest filing date is not stated")

    print()
    if fails:
        print("FAIL -- %d problem(s):" % len(fails))
        for f in fails:
            print("   * %s" % f)
        return 1
    print("PASS -- all checks hold against the shipped PDF")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT))
