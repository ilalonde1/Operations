#!/usr/bin/env python3
"""Every shared figure must agree across BOTH documents. Zero exceptions.

WHY
    The send document and the companion are built from the same section blocks,
    which stops most drift -- but not all of it. A figure re-stated in prose in
    one document and in a table in the other can diverge, and a client who reads
    both and finds two different numbers for the same thing stops believing
    either.

    They are also sent separately: the companion goes only on request, so a
    contradiction can sit unnoticed until the moment it does most damage.

WHAT IT CHECKS
    A named list of figures that appear in both documents, each with its own
    pattern. Every occurrence found anywhere in either document must carry the
    same value. Anything that does not is reported with both readings and the
    surrounding text, so the correct one can be chosen rather than guessed.

⚠ A FIGURE APPEARING IN ONLY ONE DOCUMENT IS NOT AN ERROR.
    The companion carries concentration work the send document deliberately
    omits. Only genuine disagreements fail.

USAGE
    python verify_cross_document.py [send.pdf] [companion.pdf]
"""
import io
import os
import re
import sys

import pypdf

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SEND = os.path.join(REPO, "docs", "KOR-MVE-Six-Market-Record-2026-08-28-web.pdf")
COMP = os.path.join(REPO, "docs",
                    "KOR-MVE-Six-Market-Companion-2026-08-28-web.pdf")

# label -> regex capturing the number that must agree wherever it appears
FIGURES = [
    ("Phoenix open site-plan cases", r"(\d{2,4})\s+open site-plan cases"),
    ("Phoenix cases since Jan 2025", r"(\d{2,4})\s+filed since Jan(?:uary)? 2025"),
    ("Houston plat applications", r"([\d,]{3,7})\s+plat applications"),
    ("Houston projects since 1 June", r"(\d{2,4})\s+new-construction projects registered since"),
    # ⚠ anchored to its own wording: 806 is every commercial firm in the county,
    #   45 is multifamily only. Matching a bare "N firms" conflated the two.
    ("Houston firms, all construction", r"across\s+([\d,]{3,5})\s+distinct firms"),
    ("Houston firms, multifamily", r"([\d,]{2,4})\s+firms over\s+\d+\s+projects"),
    ("Miami projects through design review", r"(\d{2,3})\s+projects through the city"),
    ("Charlotte pending petitions", r"(\d{2,3})\s+(?:pending )?rezoning petitions"),
    ("Clark County prereviews in 60 days", r"(\d{2,3})\s+multifamily (?:application )?prereviews"),
    # ⚠ the TOTAL only. "N acres" alone also matched 3,905 and 450, which are
    #   components of it, and reported a total against its own part.
    ("Howard Hughes total acreage", r"([\d,]{5,7})\s+acres in motion"),
    ("Howard Hughes plat filings", r"(?:^|\s)(\d{2})\s+plat filings"),
]

# Words that must not disagree either.
PHRASES = []


def text_of(path):
    r = pypdf.PdfReader(path)
    t = "\n".join((p.extract_text() or "") for p in r.pages)
    t = re.sub(r"\s+", " ", t)
    return re.sub(r"-\s+(?=\d)", "-", t)


def readings(text, pattern):
    out = []
    for m in re.finditer(pattern, text, re.I):
        val = m.group(1).replace(",", "").strip()
        seg = text[max(0, m.start() - 70):m.start() + 90].strip()
        out.append((val, seg))
    return out


def main(send_path, comp_path):
    send, comp = text_of(send_path), text_of(comp_path)
    print("send      : %s" % os.path.basename(send_path))
    print("companion : %s" % os.path.basename(comp_path))
    print()

    fails = []
    for label, pat in FIGURES + PHRASES:
        s_vals = readings(send, pat)
        c_vals = readings(comp, pat)
        allv = s_vals + c_vals
        if not allv:
            print("   %-40s not stated in either" % label)
            continue
        distinct = sorted({v for v, _ in allv})
        where = "both" if (s_vals and c_vals) else ("send" if s_vals else "companion")
        if len(distinct) == 1:
            print("   %-40s %-10s ok   (%s, %d mention%s)"
                  % (label, distinct[0], where, len(allv),
                     "" if len(allv) == 1 else "s"))
            continue
        print("   %-40s %-10s ** DISAGREES **" % (label, "/".join(distinct)))
        for v, seg in allv:
            src = "send" if (v, seg) in s_vals else "companion"
            print("        [%-9s] %s = %s" % (src, seg[:96], v))
        fails.append("%s: %s" % (label, " vs ".join(distinct)))

    print()
    if fails:
        print("FAIL -- %d figure(s) disagree across the two documents:" % len(fails))
        for f in fails:
            print("   * %s" % f)
        return 1
    print("PASS -- every shared figure agrees across both documents")
    return 0


if __name__ == "__main__":
    a = sys.argv[1:]
    sys.exit(main(a[0] if a else SEND, a[1] if len(a) > 1 else COMP))
