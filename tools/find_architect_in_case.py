#!/usr/bin/env python3
"""Is an architect named ANYWHERE in a planning case file? Read the DRAWINGS.

WHAT THIS FIXED, AND WHAT IT COST
    tools/resolve_project_entities.py reads a Phoenix case's CONTACT LIST and
    infers each party's role from their email domain. Good signal, insufficient
    alone. Checked against trade press and the case files themselves, two of the
    three cases that survived it as "open seats" already had an architect:

        Z-41-25-2  Fifield Companies    -> Todd & Associates Architecture
                   (not in the city file at all; named by AZBEX, 6 May 2025)
        Z-82-25-5  Elevation Living     -> Woods Associates Architects, LLC
                   (in the DRAWING TITLE BLOCK: "OWNER: ... DESIGN: ...")

    Both would have gone to a client as work that had not been let. The rule
    that catches the second one was already written down -- a plan set's title
    block names the architect -- and simply was not applied to these cases.

TWO PLACES TO LOOK, AND THE TITLE BLOCK IS THE ONE THAT PAYS
    1. A labelled team block:   ARCHITECT: / DESIGN: / DESIGN ARCHITECT:
    2. Any firm name carrying an architecture suffix, ANYWHERE in the file --
       exhibits, elevations, cover sheets. This is where it hides.

⚠ A NEGATIVE HERE IS NOT AN OPEN SEAT
    Finding a name disqualifies the lead: act on it. Finding nothing means only
    that this document does not name one. Always pair with a trade-press check,
    because Fifield's architect appears in NO city document.

USAGE
    python find_architect_in_case.py <pdf> [more.pdf ...]
"""
import io
import os
import re
import sys

import pypdf

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

# A labelled slot. "DESIGN:" is included because that is the wording Elevation
# Living's title block used, and it is what the old pattern missed.
LABEL = re.compile(
    r"(?i)\b(DESIGN ARCHITECT|PROJECT ARCHITECT|ARCHITECT|ARCHITECTURE|DESIGN)\s*:\s*"
    r"([A-Z0-9][^\n:]{2,70})")

# ⛔ CASE-SENSITIVE, AND IT MUST STAY THAT WAY.
#    A rezoning narrative says "architectural" on every page -- "consistent
#    architectural treatment", "four-sided architecture". A case-insensitive
#    pattern reads those as firm names and reports an architect on EVERY case,
#    which kills every lead and is worse than reporting none.
#
#    A real firm name in these documents is one of exactly two shapes:
#      (a) an ALL-CAPS title-block entry:  WOODS ASSOCIATES ARCHITECTS, LLC
#      (b) title case WITH a legal suffix: Todd & Associates Architects, Inc.
#    Prose never has either. Both forms tolerate the spacing PDF extraction
#    inserts inside words ("AR CHITECTS").
ARCH_WORD = r"A\s?R\s?C\s?H\s?I\s?T\s?E\s?C\s?T\s?(?:S|U\s?R\s?E)?"
FIRM = re.compile(
    r"(?:"
    r"\b([A-Z][A-Z&.'’\-]{1,20}(?:\s+[A-Z][A-Z&.'’\-]{1,20}){0,3}"
    r"\s+" + ARCH_WORD + r"(?:,?\s*(?:INC|LLC|LLP|PC|LTD|PLLC)\.?)?)\b"
    r"|"
    r"\b([A-Z][a-zA-Z&.'’\-]{1,20}(?:\s+(?:&\s+)?[A-Z][a-zA-Z&.'’\-]{1,20}){0,3}"
    r"\s+Architect(?:s|ure)?"
    r",?\s*(?:Inc|LLC|LLP|PC|Ltd|PLLC)\.?)\b"
    r")")

# Disciplines that sit next to architects and are not one.
NOT_ARCH = re.compile(
    r"(?i)landscape|golf|ASGCA|kimley|forrest richardson|civil|survey|"
    r"engineer|traffic|attorney|law\b")

# Words that mean the match is design-guideline prose, not a firm.
PROSE = re.compile(
    r"(?i)\b(shall|must|should|will be|quality|four-?sided|enhanced|integrated|"
    r"additional|overall|similar|subtle|binding|guidelines|features?|elements?|"
    r"style|character|treatment|accents?|palette|interest|commitment)\b")


def clean(s):
    s = re.sub(r"\s+", " ", s).strip(" :.-–—,")
    # rejoin letters PDF extraction spaced out
    s = re.sub(r"\bA R C H I T E C T", "ARCHITECT", s)
    s = re.sub(r"AR\s+CHITECT", "ARCHITECT", s)
    return s


def scan(path):
    r = pypdf.PdfReader(path)
    text = re.sub(r"[ \t]+", " ",
                  "\n".join((p.extract_text() or "") for p in r.pages))

    found = []
    for m in LABEL.finditer(text):
        val = clean(m.group(2))
        if not val or len(val) < 4 or PROSE.search(val) or NOT_ARCH.search(val):
            continue
        if re.match(r"(?i)^(and|or|of|the|is|are|to|for|with|shall)\b", val):
            continue
        found.append(("%s:" % m.group(1).upper(), val[:70]))

    firms = {}
    for m in FIRM.finditer(text):
        name = clean(m.group(1) or m.group(2) or "")
        if not name or NOT_ARCH.search(name) or PROSE.search(name):
            continue
        if len(name) < 10 or name.upper().startswith("ARCHITECT"):
            continue
        # a firm name has a name in front of the discipline word
        lead = re.split(r"(?i)\bARCHITECT", name)[0].strip(" ,&")
        if len(lead) < 3:
            continue
        firms[name] = firms.get(name, 0) + 1

    print("=" * 78)
    print("%-30s %d pages" % (os.path.basename(path), len(r.pages)))
    for lab, val in list(dict.fromkeys(found))[:8]:
        print("   %-18s %s" % (lab, val))
    for n, c in sorted(firms.items(), key=lambda x: -x[1])[:8]:
        print("   %-18s %-50s x%d" % ("firm name:", n[:50], c))
    hit = bool(found or firms)
    print("   %s" % ("*** ARCHITECT NAMED -- NOT AN OPEN SEAT ***" if hit
                     else "none named here (still check trade press)"))
    return hit


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    for p in sys.argv[1:]:
        scan(p)
