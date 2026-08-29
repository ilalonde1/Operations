#!/usr/bin/env python3
"""Fail the client document if it describes OUR PROCESS instead of findings.

⛔ THE BINDING RULE
    A client document carries findings, never technique. The client has never
    seen a previous version, so "six more did not survive it" describes work he
    cannot see and does not care about -- and technique is also the edge, given
    away for free.

    This kept coming back because it was removed by hand, one instance at a
    time: the exclusions box went and the KICKER above it still said the same
    thing. A gate finds all of them at once.

WHAT COUNTS AS METHOD
    First person about the work ("we checked", "we read", "we tested"), the
    verification narrative ("did not survive", "were dropped", "we removed"),
    and the names of our own techniques ("drawing title block", "trade-press
    check"). A finding about the RECORD is not method: "Chapter 343 is triggered
    by state or county involvement" stays.

USAGE
    python verify_no_method.py [send-body.html]
"""
import io
import os
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT = os.path.join(REPO, "docs", "audit-2026-08", "mve-send-body.html")

METHOD = [
    (r"\bwe (checked|read|tested|removed|dropped|verified|crossed|ran|"
     r"threw away|looked|filtered|left out|have left)\b", "first person about the work"),
    (r"did not survive|were dropped|was dropped|we would rather", "verification narrative"),
    (r"checked (?:twice|one at a time)|four tests|passed .{0,12}tests", "our test protocol"),
    (r"drawing title block|title block", "our technique"),
    (r"trade.?press check", "our technique"),
    (r"\bour (method|process|check|test)", "explicit method"),
    (r"survived|survive it", "verification narrative"),
]

# Findings about the record that merely contain a flagged word.
ALLOW = re.compile(
    r"(?i)(chapter 343|is triggered by|no architect is named because|"
    r"none is required|a site plan is a drawing|production builders)")


def strip(html):
    html = re.sub(r"(?is)<(script|style).*?</\1>", " ", html)
    t = re.sub(r"<[^>]+>", " ", html)
    for a, b in (("&mdash;", "-"), ("&rsquo;", "'"), ("&amp;", "&"),
                 ("&nbsp;", " "), ("&middot;", "-"), ("&ndash;", "-")):
        t = t.replace(a, b)
    return re.sub(r"\s+", " ", t)


def main(path):
    text = strip(io.open(path, encoding="utf-8").read())
    print("scanning %s" % os.path.basename(path))
    print()
    fails = []
    for pat, why in METHOD:
        for m in re.finditer(pat, text, re.I):
            seg = text[max(0, m.start() - 120):m.start() + 140]
            if ALLOW.search(seg):
                continue
            fails.append((m.group(0), why, seg.strip()))

    if not fails:
        print("PASS -- no process language in the client document")
        return 0

    print("FAIL -- %d instance(s) of method language:" % len(fails))
    seen = set()
    for hit, why, seg in fails:
        k = seg[:60]
        if k in seen:
            continue
        seen.add(k)
        print()
        print("   %-28s (%s)" % (hit, why))
        print("      ...%s..." % seg[:150])
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT))
