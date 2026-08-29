#!/usr/bin/env python3
"""Fail the document on anything the reader cannot resolve from the page.

⛔ THE THREE FAULTS THIS CATCHES, ALL FOUND IN ONE PARAGRAPH

  1. TRADE JARGON.  "Where not to spend the call" -- "the call" is a
     business-development idiom. The reader is an architect, not a salesperson.

  2. A DANGLING DENOMINATOR.  "the most active developer in the set", "anywhere
     in the 50". The set of what? Fifty what? Those refer to a dataset only we
     can see. Any denominator quoted has to be named in the same sentence.

  3. SELF-REFERENCE.  "the part we would want if the positions were reversed",
     "another way of saying the same thing as the first section". The reader is
     holding the document; he does not need a pointer back to page one, and he
     certainly does not need our internal reasoning about it.

    A reader who has to stop and work out what a phrase refers to has stopped
    reading the finding.
"""
import io
import os
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT = os.path.join(REPO, "docs", "audit-2026-08", "mve-send-body.html")

CHECKS = [
    (r"\bspend the call\b|\bthe call\b(?! *(?:on|,))", "trade jargon",
     "say what it is: a developer worth contacting, or not"),
    (r"\bthe ask\b|\btouch base\b|\breach out\b|\bwarm lead\b", "trade jargon",
     "plain words"),
    (r"\bin the set\b|\bof the set\b|\bthe sample\b", "dangling denominator",
     "name what the set IS, in the same sentence"),
    # ⚠ Not years. "the 2022-24 baseline" is a date range and is perfectly
    #    clear; flagging it made the gate cry wolf on correct text.
    (r"\bthe (?!(?:19|20)\d\d\b)\d{2,}\b(?!\s*(?:largest|acres|units|projects|"
     r"filings|cases|petitions|plats|applications|story|stories|sq))",
     "dangling denominator", "say fifty WHAT"),
    (r"\bour (data|dataset|records|sample|research|analysis)\b",
     "dangling denominator", "the reader cannot see our data"),
    (r"\bthe (first|second|last|previous|earlier) section\b|\bas (noted|shown) "
     r"above\b|\bearlier in this document\b|\boverleaf\b",
     "self-reference", "the reader is holding it"),
    (r"positions were reversed|if we were|we would want|from our side",
     "self-reference", "our internal reasoning is not a finding"),
]

ALLOW = re.compile(r"(?i)(the 1,426|the 373|the 281|the 92|the 84|the 66)")


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
    for pat, kind, fix in CHECKS:
        for m in re.finditer(pat, text, re.I):
            seg = text[max(0, m.start() - 90):m.start() + 110]
            if ALLOW.search(seg):
                continue
            fails.append((m.group(0).strip(), kind, fix, seg.strip()))

    if not fails:
        print("PASS -- nothing the reader has to decode")
        return 0

    print("FAIL -- %d unresolvable reference(s):" % len(fails))
    seen = set()
    for hit, kind, fix, seg in fails:
        if hit.lower() in seen:
            continue
        seen.add(hit.lower())
        print()
        print("   %-24s %s" % (hit, kind))
        print("      fix: %s" % fix)
        print("      ...%s..." % seg[:140])
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT))
