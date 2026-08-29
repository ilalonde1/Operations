#!/usr/bin/env python3
"""Audit EVERY row in the document against the two tests, in one pass.

WHY THIS EXISTS INSTEAD OF ANOTHER PATCH
    Four separate errors in this document were found by the client, not by us,
    and each was fixed on its own and re-shipped:

        1. "Final site plan" rows from December 2025 -- committed schemes.
        2. Eight preliminary site-plan rows -- filing one proves a designer.
        3. "373 site-plan and rezoning cases" -- the layer holds 0 rezonings.
        4. Bridgeland's 3,905-acre general plan -- no architect is named
           because none is REQUIRED; ~7,000 production homes follow it.

    Fixing them one at a time is the failure, not the fix. The repo's own rule:
    two regressions means stop and characterise the whole system once.

THE MODEL, WRITTEN DOWN
    A row may be offered as an OPENING only if it passes BOTH:

    TEST 1 - PREMISE.  To file this record, must a drawing of the buildings
             already exist?  If yes, a design team exists, whatever the record
             says. Site plans, design review, building permits, TDLR
             registrations all FAIL here.

    TEST 2 - COMMISSION.  Is there an architect commission in this scope at all?
             A land-subdivision instrument (general plan, street dedication,
             drainage reserve) creates lots, not buildings. Production
             single-family that follows is built from in-house plan books.
             These FAIL here even though they pass Test 1.

    And then, per row, the checks already built:
    TEST 3 - the case file / EA / site plan read for an architect, including the
             drawing title block  (tools/find_architect_in_case.py)
    TEST 4 - a trade-press check, because Fifield's architect appears in no
             city document at all
    TEST 5 - is the developer its own architect  (Kittle, True Homes, Cullum)

USAGE
    python audit_every_claim.py            # audit the send body
    python audit_every_claim.py <body.html>
"""
import io
import os
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT = os.path.join(REPO, "docs", "audit-2026-08", "mve-send-body.html")

# record type -> (needs a drawing to file, is there a commission in the scope)
RECORD = [
    (r"general plan", False, False,
     "Chapter 42 land division: streets, blocks, reserves. Production homes follow"),
    (r"street dedication|drainage|detention|open space", False, False,
     "infrastructure reserve, no building"),
    (r"site.?plan review|preliminary site plan|final site plan", True, True,
     "the drawing IS the filing"),
    (r"design review|UDRB", True, True, "the architect's own submittal"),
    (r"building permit", True, True, "construction documents"),
    (r"TDLR|state registration", True, True, "filed BY the design firm"),
    (r"rezoning|petition|PUD|special area plan", False, True,
     "land made buildable by counsel + civil + planner, before design"),
    (r"pre-?application|prereview", False, True,
     "earliest formal step, no drawing set"),
    (r"environmental assessment|EIS|EA\b", False, True,
     "prepared to win entitlements, before construction documents"),
    (r"\bplat\b|subdivision plat", False, None,
     "land division -- commission depends ENTIRELY on what is platted"),
]

VERIFIED = {  # rows we individually checked this session
    "host hotels", "copper residences", "vintage partners", "mid-america",
    "crosland", "middleburg", "dreamkey", "makena mauka", "mākena mauka",
    "hoʻonani", "hoonani", "howard hughes",
}


def classify(text):
    for pat, needs_drawing, has_commission, why in RECORD:
        if re.search(pat, text, re.I):
            return pat, needs_drawing, has_commission, why
    return None, None, None, ""


def strip(html):
    t = re.sub(r"<[^>]+>", " ", html)
    for a, b in (("&mdash;", "-"), ("&rsquo;", "'"), ("&amp;", "&"),
                 ("&nbsp;", " "), ("&ndash;", "-"), ("&middot;", "-")):
        t = t.replace(a, b)
    return re.sub(r"\s+", " ", t).strip()


def main(path):
    s = io.open(path, encoding="utf-8").read()
    print("auditing %s" % os.path.basename(path))
    print()

    problems = []
    for sec in re.finditer(r'<section id="([a-z-]+)">(.*?)</section>', s, re.S):
        sid, body = sec.group(1), sec.group(2)
        h2 = re.search(r"<h2>(.*?)</h2>", body, re.S)
        rows = re.findall(r"<tr>(?!.*?<th)(.*?)</tr>", body, re.S)
        if not rows:
            continue
        print("=" * 78)
        print("#%-10s %s" % (sid, strip(h2.group(1))[:60] if h2 else ""))
        print("-" * 78)
        for row in rows:
            txt = strip(row)
            if not txt or len(txt) < 12:
                continue
            pat, drawing, commission, why = classify(txt)
            label = (txt[:64] + "...") if len(txt) > 64 else txt
            if pat is None:
                print("   ?  %s" % label)
                continue
            offered = bool(re.search(
                r"(?i)no architect|not.{0,12}(?:appointed|engaged|named)|"
                r"open seat|unlet|worth (?:a|the) call", txt))
            verdict = "ok"
            if offered and drawing:
                verdict = "** FAILS PREMISE **"
            elif offered and commission is False:
                verdict = "** FAILS COMMISSION **"
            elif offered and commission is None:
                verdict = "check scope"
            mark = {"ok": "   ", "check scope": " ~ "}.get(verdict, " ! ")
            print("%s %-58s %s" % (mark, label[:58], verdict))
            if verdict.startswith("**"):
                problems.append((sid, label[:70], verdict, why))
        print()

    print("#" * 78)
    if problems:
        print("PROBLEMS: %d" % len(problems))
        for sid, label, verdict, why in problems:
            print("   [#%s] %s" % (sid, verdict))
            print("      %s" % label)
            print("      because: %s" % why)
        return 1
    print("No row is offered as an opening that fails either test.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT))
