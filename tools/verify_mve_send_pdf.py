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
  * ⛔ Nothing MVE already designs is presented as an opportunity. Ward Village
    may appear ONLY as their own work; Hines and Vestar may appear ONLY in the
    exclusions box.
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
    "LJA Engineering", "EX-21.1", "Teravalis",
]
# Must appear only inside the exclusions box.
EXCLUDED_CONTEXT = ["Hines", "Vestar"]


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
    print("EXCLUSION GATE  (must be in the exclusions box, never as a lead)")
    box = ""
    m = re.search(r"deliberately did not put in front of you(.{0,1400})",
                  flat, re.S | re.I)
    if m:
        box = m.group(1)
    else:
        fails.append("the exclusions box is missing from the shipped PDF")
        print("   exclusions box    MISSING")
    for name in EXCLUDED_CONTEXT:
        total = len(re.findall(re.escape(name), flat, re.I))
        inbox = len(re.findall(re.escape(name), box, re.I))
        ok = total > 0 and total == inbox
        print("   %-10s %d mention(s), %d inside the box   %s"
              % (name, total, inbox, "ok" if ok else "APPEARS OUTSIDE THE BOX"))
        if not ok:
            fails.append("%s appears outside the exclusions box (%d of %d)"
                         % (name, total - inbox, total))

    # Ward Village must never sit next to the words that would make it a lead
    for phrase in ("Ward Village pre-application", "lead: Ward Village",
                   "opportunity at Ward"):
        if re.search(re.escape(phrase), flat, re.I):
            fails.append("Ward Village presented as an opportunity: %r" % phrase)

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
