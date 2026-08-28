#!/usr/bin/env python3
"""Extract design teams from City of Miami UDRB agenda packets.

WHY THIS EXISTS
    Miami was the last region in the MVE work with no design-team source. City
    permits name the CONTRACTOR, not the architect -- the same law that holds
    everywhere else. But the Urban Development Review Board reviews every large
    project in the city, and its agenda packet is the applicant's own submittal:
    the letter of intent plus the full signed-and-sealed drawing set. The design
    team is on the document written BY the design team.

    Board 1096 on miamifl.iqm2.com. The calendar shows 37 meetings Jan 2024 to
    Dec 2026, 28 with a published packet.

        agenda packet   FileOpen.aspx?Type=1&ID=<n>    48-505 MB   HAS the team
        bare agenda     FileOpen.aspx?Type=14&ID=<n>   ~330 KB     no architect
        fact sheet      FileOpen.aspx?Type=30&ID=<n>   ~126 KB     no architect
        one item        FileOpen.aspx?Type=4&ID=<n>    ~78 MB      HAS the team

    Download the packet, `pdftotext` it, point this at the .txt.

THREE SIGNALS, BECAUSE NO ONE OF THEM IS UNIVERSAL
    1. COPYRIGHT BLOCK on every sheet -- "(c) 2025 BEHAR FONT PARTNERS, P.A.
       THE DESIGN AND DRAWINGS FOR THIS PROJECT ARE PROPERTY OF THIS ARCHITECT".
       The same signal that works in Raleigh. Most reliable, and it repeats once
       per sheet so frequency ranks it.
    2. STACKED ROLE BLOCK on the cover sheet -- a bare "ARCHITECTS" heading with
       the firm on the following line. Present in some packets, absent in others.
    3. LETTER-OF-INTENT PROSE -- "as prepared by X", "designed by X".

TRAPS PAID FOR ALREADY
    * THE COPYRIGHT BLOCK CATCHES THE LANDSCAPE ARCHITECT TOO. One packet gave
      CUBE 3 (architect, ~45 sheets) and WITKIN HULTS + PARTNERS (landscape, 8).
      Rank by frequency and screen the name -- do not take the first match.
    * ANCHOR WORDING VARIES between meetings: "PROJECT KNOWN AS PZ-25-19794 FOR
      OKO LILLI TOWER LOCATED AT ..." in one packet, "THE PROJECT KNOWN AS
      PZ2519175 LOCATED AT ..." -- no hyphens, no project name -- in the next.
    * The (c) glyph usually survives extraction as U+FFFD, not U+00A9.
    * A packet holds several projects. Item tags (5.1.a, 5.2.a ...) delimit each
      one's pages and are the attribution key -- without them a firm from one
      project is silently credited to another.
    * SANITY-CHECK EVERY FIRM against what it actually does before quoting it.
      Engineers, landscape architects and surveyors all sit in these documents.

USAGE
    python udrb_teams.py <packet.txt> [more.txt ...]
    python udrb_teams.py --tsv out.tsv udrb_txt/*.txt
"""
import re
import sys
from collections import Counter, defaultdict

ITEM = re.compile(r"^(\d+\.\d+\.[a-z])$")

# Wording varies between meetings: "PZ-25-19794 FOR OKO LILLI TOWER LOCATED AT"
# in one packet, "PZ-25-19047, A WAIVER, FOR THE PROPERTY LOCATED AT" in the
# next. Allow anything between the number and LOCATED AT.
ANCHOR = re.compile(
    r"(?i)PROJECT\s+KNOWN\s+AS\s+(PZ[\-\s]?\d[\d\-]*)"
    r"(?P<mid>[^\n]{0,70}?)"
    r"\s+LOCATED\s+AT\s+(?P<addr>.{5,90}?)(?:,\s*MIAMI|\.|$)")

# The (c) glyph normally arrives as U+FFFD from pdftotext. Require the block's
# own boilerplate to follow: without it a stray "(c)" captures running text
# ("OR APPROVED EQUAL" scored a copyright hit before this was tightened).
COPYRIGHT = re.compile(
    r"(?:©|�|\(c\))\s*(?:\d{4})?\s*,?\s*"
    r"(?P<firm>[A-Z][A-Za-z0-9&'\+\.\, \-]{2,55}?)"
    r"\s*(?:,\s*)?(?=ALL RIGHTS|THE DESIGN|EXPRESSLY|COPYRIGHT)")

ROLE_HEAD = re.compile(r"(?i)^\s*(ARCHITECTS?|ARCHITECT OF RECORD|"
                       r"DESIGN ARCHITECT)\s*:?\s*$")
# Any role caption, used to step over a second label sitting where the firm
# should be ("Architect:" immediately followed by "Landscape Architect:").
ROLE_LABEL = re.compile(r"(?i)^\s*(landscape|civil|structural|mechanical|"
                        r"electrical|traffic|design|project|mep)?\s*"
                        r"(architects?|engineers?|consultants?|surveyors?|"
                        r"owner|developer|applicant|contractor)\s*:?\s*$")
PROSE = re.compile(
    r"(?i)\b(?:architects?\s+of\s+record|(?:as\s+)?(?:prepared|designed|"
    r"authored)\s+by)\s+"
    r"(?P<firm>[A-Z][A-Za-z0-9&'\+\.\, \-]{3,60}?)"
    r"(?=\s*(?:\(|,\s*(?:the|as)\b|\.|;|$))")

# Screens. These do not decide anything on their own -- they demote a candidate
# so a landscape or engineering firm cannot outrank the architect by accident.
NOT_ARCHITECT = re.compile(
    r"(?i)\b(landscape|civil|structural|geotechnical|surveying|surveyors?|"
    r"traffic|mechanical|electrical|plumbing|mep\b|engineering|engineers?|"
    r"consultants?|contracting|construction|realty|development|holdings|"
    r"capital|partners? llp\b|attorneys?|law\b)\b")
ARCHITECT_HINT = re.compile(r"(?i)\b(architect|architecture|arquitect|"
                            r"design studio|studio|atelier)\b")
JUNK = re.compile(r"(?i)^(packet pg|page \d|sheet|scale|north|drawn|checked|"
                  r"date|rev|no\.|project no)")


def clean_firm(raw):
    f = re.sub(r"\s+", " ", (raw or "")).strip(" ,.-")
    f = re.sub(r"(?i)\s*(all rights reserved|copyright).*$", "", f).strip(" ,.-")
    return f


def looks_like_firm(f):
    """Reject extraction debris before it can be ranked as a firm.

    Vector title blocks extract as things like 'GGGG331HG22GH213' and "I33V'3V",
    which are not names -- they are collapsed glyph runs.
    """
    if not f or not (4 <= len(f) <= 60) or JUNK.match(f):
        return False
    if len(re.findall(r"\d", f)) >= 3:
        return False
    letters = re.findall(r"[A-Za-z]", f)
    if len(letters) < 4 or len(letters) / len(f) < 0.55:
        return False
    if not re.search(r"[AEIOUaeiou]", f):
        return False
    words = [w for w in re.split(r"[^A-Za-z&]+", f) if len(w) >= 2]
    return len(words) >= 2 or bool(ARCHITECT_HINT.search(f))


def segments(lines):
    """Yield (item_tag, [lines]).

    The tag is a MARGIN STAMP on each page and lands after that page's body in
    the extracted stream, so a line belongs to the NEXT tag at or below it, not
    the previous one. Attributing to the previous tag put 619 Brickell's letter
    of intent under the OKO Lilli item.
    """
    buf = []
    for ln in lines:
        m = ITEM.match(ln.strip())
        if m:
            yield m.group(1), buf
            buf = []
            continue
        buf.append(ln)
    if buf:
        yield "(trailing)", buf


def candidates(text):
    """firm -> {evidence type -> count}, from all three signals."""
    found = defaultdict(Counter)
    for m in COPYRIGHT.finditer(text):
        f = clean_firm(m.group("firm"))
        if not looks_like_firm(f):
            continue
        # A landscape architect's sheets carry a copyright block of their own.
        # WITKIN HULTS + PARTNERS outranked the architect on two items before
        # this looked at the block's surroundings; the name alone never says so.
        near = text[max(0, m.start() - 200):m.end() + 200].upper()
        kind = "copyright_landscape" if "LANDSCAPE ARCHITECT" in near else "copyright"
        found[f][kind] += 1
    lines = text.split("\n")
    for i, ln in enumerate(lines):
        if ROLE_HEAD.match(ln):
            for nxt in lines[i + 1:i + 4]:
                # "Architect:" is sometimes followed by "Landscape Architect:" --
                # another label, not the firm. Step over labels.
                if ROLE_LABEL.match(nxt.strip()):
                    continue
                f = clean_firm(nxt)
                if looks_like_firm(f):
                    found[f]["role_block"] += 1
                    break
    for m in PROSE.finditer(text):
        f = clean_firm(m.group("firm"))
        if not looks_like_firm(f):
            continue
        # Same context test as the copyright block. "prepared by Fortin, Leavy,
        # Skiles" sits on the ALTA survey sheet -- surveyors, not the architect.
        near = text[max(0, m.start() - 200):m.end() + 200].upper()
        if "LANDSCAPE ARCHITECT" in near:
            found[f]["prose_landscape"] += 1
        elif "SURVEY" in near and "ARCHITECT" not in near:
            found[f]["prose_survey"] += 1
        else:
            found[f]["prose"] += 1
    return found


def score(firm, ev):
    """Rank candidates. Evidence type first, then how many sheets carry it."""
    s = 0
    if ev.get("role_block"):
        s += 400
    if ev.get("copyright"):
        s += 300
    if ev.get("prose"):
        s += 150
    if not ev.get("copyright") and not ev.get("role_block") and not ev.get("prose"):
        if ev.get("copyright_landscape") or ev.get("prose_landscape") \
                or ev.get("prose_survey"):
            s -= 450
    s += min(sum(ev.values()), 120)
    if ARCHITECT_HINT.search(firm):
        s += 120
    if NOT_ARCHITECT.search(firm):
        s -= 500
    return s


def pick(text):
    cands = candidates(text)
    if not cands:
        return None, [], {}
    ranked = sorted(cands.items(), key=lambda kv: -score(kv[0], kv[1]))
    best, ev = ranked[0]
    if score(best, ev) <= 0:
        return None, ranked, cands
    return best, ranked, cands


def run(paths, tsv=None):
    out = []
    for path in paths:
        text = open(path, encoding="utf-8", errors="replace").read()
        lines = text.split("\n")
        anchors = []
        seen = set()
        for m in ANCHOR.finditer(re.sub(r"\s+", " ", text)):
            pz = re.sub(r"[\s\-]", "", m.group(1)).upper()
            if pz in seen:
                continue
            seen.add(pz)
            mid = m.group("mid") or ""
            nm = re.search(r"(?i)\bFOR\s+(?!THE PROPERTY\b|PROPERTY\b)(.+)$", mid)
            anchors.append((pz, clean_firm(nm.group(1) if nm else ""),
                            clean_firm(m.group("addr") or "")))
        print("\n=== %s -- %d project(s) on the agenda" % (path, len(anchors)))
        for pz, nm, addr in anchors:
            print("    %-12s %-34s %s" % (pz, nm[:34] or "(unnamed)", addr[:48]))

        segs = [(tag, buf) for tag, buf in segments(lines)
                if tag != "(front matter)"]
        merged = defaultdict(list)
        for tag, buf in segs:
            merged[tag].extend(buf)
        for tag in sorted(merged):
            body = "\n".join(merged[tag])
            firm, ranked, _ = pick(body)
            ev = dict(ranked[0][1]) if ranked and firm else {}
            runners = ", ".join("%s(%d)" % (f, sum(e.values()))
                                for f, e in ranked[1:4]) if ranked else ""
            print("  item %-7s -> %-42s %-28s" %
                  (tag, firm or "(NOT RECOVERED)", ev or ""))
            if runners:
                print("        also seen: %s" % runners[:150])
            out.append((path, tag, firm or "", str(ev), runners))
    if tsv:
        with open(tsv, "w", encoding="utf-8") as fh:
            fh.write("packet\titem\tarchitect\tevidence\talso_seen\n")
            for row in out:
                fh.write("\t".join(row) + "\n")
        print("\nwrote %s (%d rows)" % (tsv, len(out)))


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        raise SystemExit(2)
    tsv = None
    if args[0] == "--tsv":
        tsv, args = args[1], args[2:]
    run(args, tsv)
