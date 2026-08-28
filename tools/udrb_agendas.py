#!/usr/bin/env python3
"""Build the Miami project list from UDRB agendas -- the pipeline answer.

WHY THIS EXISTS, SEPARATELY FROM udrb_teams.py
    Two different questions need two different documents.

    "Who is my competition and how concentrated is it" is a STRUCTURAL question.
    It needs the design team, which is only in the agenda PACKET (48-505 MB),
    and it needs depth -- one meeting cannot distinguish "this firm keeps
    winning" from "this firm won twice".

    "What is in the pipeline" is a CURRENT question, and the answer is the bare
    AGENDA: 330 KB, project name, address and PZ number for everything the board
    is about to hear. Downloading gigabytes of drawing sets to answer it would be
    the wrong document at 400x the cost.

    This tool does the second one. It is the Miami equivalent of the Phoenix
    submitted-projects search: what has been filed, when, and where.

USAGE
    python udrb_agendas.py <meetings.tsv> <outdir>          # fetch + parse
    python udrb_agendas.py --parse-only <outdir>

    meetings.tsv is date/packet/agenda/meeting/cancelled, as produced from the
    IQM2 calendar (Calendar.aspx?From=...&To=...), agenda id in column 3.

NOTE
    An agenda names the project, not the architect -- that is in the packet.
    Checked directly: 0 architect mentions in a 2-page agenda. Do not expect one.
"""
import os
import re
import subprocess
import sys
import urllib.request

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0")
AGENDA = "https://miamifl.iqm2.com/Citizens/FileOpen.aspx?Type=14&ID=%s"

# Do NOT try to anchor on the case number's position. Reconciling against the
# agendas' own resolution counts turned up at least five wordings:
#     PROJECT KNOWN AS PZ-24-17559 LOCATED AT ...
#     PROJECT KNOWN AS-PZ-25-19006 LOCATED AT ...      (hyphen glued to AS)
#     PROJECT KNOWN AS- PZ-24-18644 LOCATED AT ...
#     KNOWN AS HIGHLAND PARK AND PZ-24-18618 LOCATED AT ...   (name FIRST)
#     PROJECT KNOWN AS ADELA II AND PZ-2316446 LOCATED ...    (one hyphen)
# So capture the whole span between KNOWN AS and LOCATED AT and search inside it.
# "LOCATED AT" is not dependable either. Seen in the wild:
#     ... PZ-22-15653-LOCATEDE AT 535, 543 ...   (typo in the agenda)
#     ... AND PZ-24-17552 LOCATED 422 NORTHEAST 29TH STREET   (no "AT")
# so allow a couple of stray letters on LOCATED and make AT optional. The span
# also runs longer than expected when the project name is a long one
# ("BRAMAN MIAMI SPECIAL AREA PLAN AND PZ-22-15092").
# The address terminator must not require a comma BEFORE "MIAMI": agendas write
# both "...STREET, MIAMI, FLORIDA" and "...BISCAYNE BOULEVARD MIAMI, FLORIDA".
# Requiring the comma made four agendas parse to zero, because the text is
# flattened to a single line first so "$" never rescues the match.
ITEM = re.compile(
    r"(?i)KNOWN\s+AS(?P<mid>.{0,170}?)[\s,]+LOCATED\w{0,2}\s+(?:AT\s+)?"
    r"(?P<addr>.{5,90}?)(?:[,\s]\s*MIAMI\b|\.|$)")
PZNUM = re.compile(r"(?i)PZ[\s\-]{0,2}(\d[\d\-]*)")
STRIP = re.compile(r"(?i)^(the\s+)?(project\s+)?(known\s+as)?[\s\-:,]*|"
                   r"[\s\-:,]*(and|for|the\s+project)?[\s\-:,]*$")


def fetch(agenda_id, outdir):
    pdf = os.path.join(outdir, "agenda_%s.pdf" % agenda_id)
    txt = os.path.join(outdir, "agenda_%s.txt" % agenda_id)
    if os.path.exists(txt) and os.path.getsize(txt) > 200:
        return txt
    req = urllib.request.Request(AGENDA % agenda_id, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=120) as r, open(pdf, "wb") as fh:
        fh.write(r.read())
    subprocess.run(["pdftotext", "-q", pdf, txt], timeout=300)
    os.remove(pdf)
    return txt


def parse(txt_path, when=""):
    text = re.sub(r"\s+", " ", open(txt_path, encoding="utf-8",
                                    errors="replace").read())
    out, seen = [], set()
    for m in ITEM.finditer(text):
        mid = m.group("mid") or ""
        pzm = PZNUM.search(mid)
        if not pzm:
            continue
        pz = "PZ" + re.sub(r"\D", "", pzm.group(1))
        if pz in seen:
            continue
        seen.add(pz)
        # Whatever is left of the span, once the case number is removed, is the
        # project's name -- it sits before the number as often as after it.
        name = (mid[:pzm.start()] + " " + mid[pzm.end():])
        name = STRIP.sub("", re.sub(r"\s+", " ", name)).strip(" ,.-")
        out.append({
            "meeting": when,
            "pz": pz,
            "project": name,
            "address": m.group("addr").strip(" ,."),
        })
    return out


def reconcile(txt_path, got):
    """The agenda states its own item count -- assert the parse matches it.

    A parser must prove itself against the source's own labels, never against
    its own output. Under-recovery here looks exactly like a quiet market.
    """
    text = re.sub(r"\s+", " ", open(txt_path, encoding="utf-8",
                                    errors="replace").read())
    # Count DISTINCT case numbers, not resolution paragraphs. An agenda states
    # each item several times -- summary, detail and resolution text -- so the
    # December 2024 agenda has 12 resolution paragraphs for 3 real projects.
    # Counting paragraphs reported the parser as 9 short when it was correct,
    # and very nearly had me "fix" working code.
    stated = {"PZ" + re.sub(r"\D", "", m.group(1))
              for m in PZNUM.finditer(text)}
    return len(stated), got


def main():
    if sys.argv[1] == "--parse-only":
        outdir = sys.argv[2]
        pairs = [(f, "") for f in sorted(os.listdir(outdir))
                 if f.endswith(".txt")]
        rows = []
        for f, _ in pairs:
            rows += parse(os.path.join(outdir, f))
    else:
        tsv, outdir = sys.argv[1], sys.argv[2]
        os.makedirs(outdir, exist_ok=True)
        rows = []
        for line in list(open(tsv, encoding="utf-8"))[1:]:
            p = line.rstrip("\n").split("\t")
            if len(p) < 4 or not p[2]:
                continue
            date, agenda_id = p[0], p[2]
            try:
                txt = fetch(agenda_id, outdir)
                got = parse(txt, date)
                rows += got
                stated, _ = reconcile(txt, len(got))
                flag = "" if len(got) >= stated else "  <-- SHORT by %d" % (
                    stated - len(got))
                print("  %-22s agenda %-6s resolutions=%-3d parsed=%-3d%s"
                      % (date, agenda_id, stated, len(got), flag))
                sys.stdout.flush()
            except Exception as e:
                print("  FAIL %s %s :: %s" % (date, agenda_id, str(e)[:60]))

    print("\n%d project items across the agendas" % len(rows))
    uniq = {}
    for r in rows:
        uniq.setdefault(r["pz"], r)
    print("%d distinct PZ cases\n" % len(uniq))
    for r in sorted(uniq.values(), key=lambda r: r["pz"], reverse=True):
        print("  %-12s %-34s %-44s %s" %
              (r["pz"], (r["project"] or "")[:34], r["address"][:44],
               r["meeting"][:12]))


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__)
        raise SystemExit(2)
    main()
