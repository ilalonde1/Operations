#!/usr/bin/env python3
"""Miami's EARLY layer: the Planning, Zoning and Appeals Board.

WHY THIS AND NOT THE URBAN DEVELOPMENT REVIEW BOARD
    tools/udrb_agendas.py reads board 1096, the Urban Development Review Board.
    UDRB reviews the ARCHITECT'S OWN DRAWINGS. Every record it produces is
    therefore dated after the commission was awarded -- excellent for measuring
    who wins work in Miami, useless for finding work that has not been let.

    Board 1037, the Planning, Zoning and Appeals Board, hears the step before:
    rezonings, land use changes, special area plans, variances and exceptions.
    These are filed by the owner's land use attorney to make a site buildable,
    which happens BEFORE a building is designed. And the bare agenda states it
    outright, in a fixed field:

        LOCATION:     approximate address and the city commissioner's district
        APPLICANT(S): the attorney, and the entity they act for
        PURPOSE:      what is being asked for, in plain words
        FINDING(S):   the planning department's recommendation

    In Miami the land use attorney is the gatekeeper, so "who filed it" is as
    useful as "who owns it" -- often more so, because the attorney is a durable
    relationship and the ownership entity is a fresh LLC each time.

    Board ids come from Calendar.aspx's meeting-group dropdown:
        1037  Planning, Zoning and Appeals Board   <- this tool
        1096  Urban Development Review Board       <- udrb_agendas.py
        1040  Historic and Environmental Preservation Board
        1100  Wynwood Design Review Committee
        1000  City Commission

    Documents hang off Detail_Meeting.aspx?ID=<meeting> as
    FileOpen.aspx?Type=14&ID=<agenda>. Type 14 is the bare agenda -- a few
    hundred KB, and it carries everything above. Do NOT pull Type=1 packets for
    this question; they are the same order of size as the UDRB packets and
    contain drawings you do not need.

⚠ LIMITS
    * PZAB does not sit in August. The July agenda is the newest of the summer
      and that is the board's calendar, not a stale feed.
    * An APPLICANT on a rezoning is not necessarily the eventual developer; land
      is often entitled and then sold. Say "filed by", not "is building".
    * Appeals and code items appear on the same agenda as rezonings. Filter on
      the purpose text, not the item number.
    * "Not named" is not "not appointed" -- the same rule as every other market
      in this work.

USAGE
    python miami_pzab_agendas.py meetings                  # id -> date, docs
    python miami_pzab_agendas.py fetch  [dir] [year]
    python miami_pzab_agendas.py items  [dir] [out.json]
"""
import io
import json
import os
import re
import sys
import time
import urllib.request

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

BOARD = "Planning,? *Zoning *and *Appeals"
CAL = ("https://miamifl.iqm2.com/Citizens/Calendar.aspx?From=1/1/%s&To=12/31/%s")
MEET = "https://miamifl.iqm2.com/Citizens/Detail_Meeting.aspx?ID=%s"
AGENDA = "https://miamifl.iqm2.com/Citizens/FileOpen.aspx?Type=14&ID=%s"
UA = {"User-Agent": ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                     "AppleWebKit/537.36 (KHTML, like Gecko) "
                     "Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0")}


def get(url, binary=False):
    r = urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=240)
    d = r.read()
    return d if binary else d.decode("utf-8", "replace")


def meetings(year="2026"):
    html = get(CAL % (year, year))
    rows = re.findall(
        r"(?is)<div[^>]*class=\"[^\"]*MeetingRow[^\"]*\"[^>]*>(.*?)</div>\s*</div>",
        html)
    ids = []
    for r in rows:
        if not re.search("(?i)" + BOARD, r):
            continue
        m = re.search(r"Detail_Meeting\.aspx\?ID=(\d+)", r)
        if m:
            ids.append(m.group(1))
    out = []
    for mid in ids:
        h = get(MEET % mid)
        t = re.search(r"(?is)<title>(.*?)</title>", h)
        title = t.group(1).strip() if t else ""
        date = title.split(" ")[0] if title else "?"
        docs = dict((ty, i) for ty, i in
                    re.findall(r"FileOpen\.aspx\?Type=(\d+)&amp;ID=(\d+)", h))
        out.append({"meeting": mid, "date": date, "agenda": docs.get("14")})
        time.sleep(0.25)
    out.sort(key=lambda r: r["date"])
    return out


def fetch(dest="miami-pzab", year="2026"):
    os.makedirs(dest, exist_ok=True)
    ms = meetings(year)
    print("%d %s PZAB meetings; %d with a published agenda"
          % (len(ms), year, sum(1 for m in ms if m["agenda"])))
    for m in ms:
        if not m["agenda"]:
            print("  %-12s meeting %-6s (no agenda published)"
                  % (m["date"], m["meeting"]))
            continue
        p = os.path.join(dest, "%s-pzab-%s.pdf"
                         % (m["date"].replace("/", "-"), m["agenda"]))
        if not os.path.exists(p):
            with open(p, "wb") as fh:
                fh.write(get(AGENDA % m["agenda"], binary=True))
            time.sleep(0.4)
        print("  %-12s meeting %-6s agenda %-6s %8d bytes"
              % (m["date"], m["meeting"], m["agenda"], os.path.getsize(p)))
    with open(os.path.join(dest, "meetings.json"), "w", encoding="utf-8") as fh:
        json.dump(ms, fh, indent=1)
    return ms


FIELD = re.compile(r"^(LOCATION|APPLICANT\(S\)|APPELLANT\(S\)|PURPOSE|"
                   r"FINDING\(S\)|ITEM)\s*:", re.I)


def parse(path):
    import pypdf
    r = pypdf.PdfReader(path)
    txt = "\n".join((p.extract_text() or "") for p in r.pages)
    lines = [l.strip() for l in txt.split("\n") if l.strip()]
    date = re.search(r"(\w+ \d{1,2}, \d{4})", txt)
    items, cur, field, buf = [], None, None, []

    def flush():
        if cur is not None and field:
            cur[field.lower()] = re.sub(r"\s+", " ", " ".join(buf)).strip()

    for l in lines:
        if re.match(r"^\d{4,6}$", l):          # the item's file number
            flush()
            if cur:
                items.append(cur)
            cur = {"file_no": l, "date": date.group(1) if date else "",
                   "source": os.path.basename(path)}
            field, buf = None, []
            continue
        m = FIELD.match(l)
        if m and cur is not None:
            flush()
            field = m.group(1).replace("(S)", "").upper()
            buf = [l[m.end():].strip()]
        elif field and cur is not None:
            buf.append(l)
    flush()
    if cur:
        items.append(cur)
    # strip page furniture that slipped into a field
    for it in items:
        for k, v in list(it.items()):
            if isinstance(v, str):
                it[k] = re.sub(r"City of Miami Page \d+ Printed on \S+", "", v).strip()
    return [i for i in items if i.get("applicant") or i.get("purpose")]


def items(d="miami-pzab", out=None):
    got = []
    for f in sorted(os.listdir(d)):
        if f.endswith(".pdf"):
            got.extend(parse(os.path.join(d, f)))
    print("%d agenda items with an applicant or purpose" % len(got))
    print()
    for it in got:
        print("-" * 78)
        print("  %-14s file %s" % (it.get("date", "")[:14], it["file_no"]))
        for k in ("location", "applicant", "purpose"):
            if it.get(k):
                print("     %-10s %s" % (k, it[k][:96]))
    if out:
        with open(out, "w", encoding="utf-8") as fh:
            json.dump(got, fh, indent=1, ensure_ascii=False)
        print("\nwrote %s" % out)
    return got


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "items"
    a = sys.argv[2:]
    if cmd == "meetings":
        for m in meetings(*(a or ["2026"])):
            print("  %-12s meeting %-7s agenda %s"
                  % (m["date"], m["meeting"], m["agenda"]))
    elif cmd == "fetch":
        fetch(*(a or ["miami-pzab", "2026"]))
    elif cmd == "items":
        items(*(a or ["miami-pzab"]))
    else:
        raise SystemExit(__doc__)
