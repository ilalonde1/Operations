#!/usr/bin/env python3
"""Refuse to offer as an opening any record type that PROVES a designer exists.

⛔ WHY THIS EXISTS — READ THIS BEFORE ADDING A SOURCE

    Every other verifier in this repo checks whether a NUMBER REPRODUCES. Not
    one of them asked whether the thing being shown was the right thing to show.
    So a row reading "9 Dec 2025 | Sierra Verde Townhomes | Final site plan"
    passed every gate: it was accurate, and useless. And "373 open site-plan and
    rezoning cases" passed because 373 was correct — nobody checked the noun.
    (The layer holds 373 site-plan cases and ZERO rezonings.)

    The client caught both, three times, along with the premise underneath them:

        A site plan is a drawing. To file one, somebody must have drawn the
        buildings. So EVERY site-plan case already has a design team, whatever
        the record's professional field says. Preliminary vs final changes how
        much is still moveable; it does not change whether an architect exists.

    That is one test that kills whole CATEGORIES at once, instead of catching
    rows one at a time. It is the same test that had already ruled out Houston's
    TDLR registrations (the design firm files them) and Miami's UDRB packets
    (they are the architect's own drawings) — applied to two markets and not the
    third.

THE CLASSIFICATION
    For every source, answer one question: TO FILE THIS, MUST A DRAWING OF THE
    BUILDINGS ALREADY EXIST?

        yes -> the design team exists. Quote it for market activity, for
               concentration, for who-won-what. NEVER as an opening.
        no  -> it may be an opening, subject to the per-lead architect check in
               tools/find_architect_in_case.py plus a trade-press check.

USAGE
    python verify_record_premise.py [pdf]
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

# (record type, drawings required to file it?, why)
RECORD_TYPES = [
    # ⚠ Match the CASE TYPE, not the words "site plan".
    #    A Phoenix "site plan review" case is a review of designed buildings.
    #    A rezoning petition can also carry a "site plan" -- a schematic showing
    #    envelope and density, drawn by the civil engineer or land planner for
    #    zoning purposes, and not an architectural commission. Crosland
    #    Southeast's is so early that the ENGINEER'S seal block is still blank.
    #    Treating both as the same thing fails a legitimate lead.
    ("site plan review", True,
     "a site-plan review case reviews designed buildings"),
    ("preliminary site plan", True,
     "a filed site plan IS the drawing; a designer is engaged"),
    ("final site plan", True,
     "a final site plan is a committed scheme"),
    ("design review", True,
     "design review reviews the architect's own drawings"),
    ("building permit", True,
     "construction documents, years past the appointment"),
    ("TDLR", True,
     "a Texas registration is filed BY the design firm"),
    ("UDRB", True,
     "the architect's own submittal"),
    ("rezoning", False,
     "filed by land-use counsel with civil and a land planner, before design"),
    ("pre-application", False,
     "earliest formal step; no drawing set exists"),
    ("prereview", False, "same"),
    ("plat", False,
     "a land division filed by the developer's surveyor or engineer"),
    ("environmental assessment", False,
     "prepared to win entitlements, before construction documents"),
]

# Language that offers something as unlet work.
# ⚠ "lead" alone is far too loose. It matched "Nobody LEADS it: Arquitectonica
#    and Kobi Karp hold six apiece" -- a concentration measurement, which is the
#    CORRECT use of a design-review record. Match the sales noun, not the verb.
OPENING = re.compile(
    r"(?i)(open seat|no architect|architect is not|not yet (?:been )?"
    r"(?:engaged|appointed|hired)|unlet|has not been let|worth (?:a|the) call|"
    r"\ban opening\b|\bopenings\b|\ba lead\b|\bleads worth\b)")

# ⚠ A GATE THAT CANNOT TELL ASSERTION FROM EXPLANATION IS USELESS.
#    The document has to be able to SAY "a site plan is a drawing, so none of
#    those 373 is unlet work" -- that sentence contains both "site plan" and
#    "unlet" and is the exact opposite of the error being guarded against.
#    Likewise "the case file or the site plan read in full" describes the CHECK.
#    So a nearby explanatory marker clears the hit. Rows do not read like this;
#    prose does.
EXPLAINING = re.compile(
    r"(?i)(is a drawing|are drawings|already drawn|have a design team|"
    r"proves|proof|read in full|close to useless|none of (?:those|these) is|"
    r"do not offer|never as an opening|not an opening|which is why we|"
    r"cannot be acted|checked twice|did not survive|we removed|"
    r"whatever the record)")


def main(path):
    r = pypdf.PdfReader(path)
    text = re.sub(r"\s+", " ", "\n".join((p.extract_text() or "")
                                         for p in r.pages))
    print(os.path.basename(path))
    print("  %d pages" % len(r.pages))
    print()

    fails = []
    print("%-26s %-9s %s" % ("RECORD TYPE", "DRAWINGS", "OFFERED AS AN OPENING?"))
    for term, needs_drawings, why in RECORD_TYPES:
        hits = [m.start() for m in re.finditer(re.escape(term), text, re.I)]
        if not hits:
            print("   %-23s %-9s not mentioned"
                  % (term, "yes" if needs_drawings else "no"))
            continue
        bad = []
        for h in hits:
            seg = text[max(0, h - 200):h + 200]
            if OPENING.search(seg) and not EXPLAINING.search(seg):
                bad.append(seg)
        if needs_drawings and bad:
            print("   %-23s %-9s ** %d NEARBY OPENING CLAIM(S) **"
                  % (term, "yes", len(bad)))
            for seg in bad[:1]:
                print("        ...%s..." % seg.strip()[:150])
            fails.append("%r is offered as an opening, but %s" % (term, why))
        else:
            note = "ok" if needs_drawings else "ok (may be an opening)"
            print("   %-23s %-9s %s   (%d mention%s)"
                  % (term, "yes" if needs_drawings else "no", note,
                     len(hits), "" if len(hits) == 1 else "s"))

    print()
    print("NOUN CHECK")
    # The specific conflation that shipped: the Phoenix layer has no rezonings.
    for bad_noun in ["site-plan and rezoning", "site plan and rezoning"]:
        n = len(re.findall(re.escape(bad_noun), text, re.I))
        print("   %-28s %d   %s" % (bad_noun, n, "ok" if n == 0 else
                                    "PRESENT - the layer holds 0 rezonings"))
        if n:
            fails.append("%r: the Phoenix layer contains no rezoning records"
                         % bad_noun)

    print()
    if fails:
        print("FAIL -- %d problem(s):" % len(fails))
        for f in dict.fromkeys(fails):
            print("   * %s" % f)
        return 1
    print("PASS -- no record type is offered as something it disproves")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT))
