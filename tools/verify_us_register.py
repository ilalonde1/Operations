#!/usr/bin/env python3
"""Fail the document if it reads British to an American client.

⛔ WHY
    The reader is an architect in Irvine, California. "Nine SCHEMES in your
    markets" reads to him as a plot, not a project -- "scheme" is British trade
    usage for a development and carries the wrong connotation in US English.
    One word like that makes a document feel foreign, and a document that feels
    foreign feels less credible whatever it says.

    Once one appears, others will: the same hand writes "storey", "sitting",
    "fortnight" and "-ise" endings without noticing. So this checks all of them
    at once rather than fixing the one that got spotted.

⚠ NOT EVERY MATCH IS WRONG
    "Petitioner", "counsel", "entitlement" and "acreage" are US planning terms
    and stay. Proper nouns are exempt -- a firm may legitimately be called
    "Centre Street Partners".
"""
import io
import os
import re
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT = os.path.join(REPO, "docs", "audit-2026-08", "mve-send-body.html")

BRITISH = [
    (r"\bschemes?\b", "project / development"),
    (r"\bstoreys?\b", "story / stories"),
    (r"\bfortnight\b", "two weeks"),
    (r"\bsitting\b(?! *(?:on|in) )", "meeting"),
    (r"\bwhilst\b", "while"),
    (r"\bamongst\b", "among"),
    (r"\btowards\b", "toward"),
    (r"\blearnt\b", "learned"),
    (r"\bprogramme\b", "program"),
    (r"\bcentres?\b", "center"),
    (r"\bmetres?\b", "meter"),
    (r"\bcolour", "color"),
    (r"\bbehaviour", "behavior"),
    (r"\blabelled\b", "labeled"),
    (r"\btravelling\b", "traveling"),
    (r"\borganis(e|ed|ation)\b", "organiz-"),
    (r"\brecognis(e|ed)\b", "recogniz-"),
    (r"\bcharacteris(e|ed)\b", "characteriz-"),
    (r"\bpractice\b(?= *(?:that|which|,|\.))", "firm"),
    (r"\benquir", "inquir"),
    (r"\bcheque\b", "check"),
    (r"\bstorey-", "story-"),
    (r"\bper cent\b", "percent"),
    (r"\bhalf an acre\b", "half-acre (fine, but check register)"),
]

# Legitimate US planning vocabulary that can look British.
ALLOW = re.compile(r"(?i)(petitioner|counsel|entitlement|acreage|reserve)")


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
    for pat, better in BRITISH:
        hits = [m for m in re.finditer(pat, text)]
        if not hits:
            continue
        clean = []
        for m in hits:
            seg = text[max(0, m.start() - 60):m.start() + 80]
            if ALLOW.search(seg):
                continue
            clean.append((m.group(0), seg))
        if clean:
            print("   %-18s x%-3d -> %s" % (clean[0][0], len(clean), better))
            print("        ...%s..." % clean[0][1].strip()[:120])
            fails.append((clean[0][0], len(clean), better))

    print()
    if fails:
        print("FAIL -- %d British usage(s) for an American reader"
              % sum(n for _, n, _ in fails))
        return 1
    print("PASS -- reads American")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else DEFAULT))
