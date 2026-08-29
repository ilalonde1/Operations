#!/usr/bin/env python3
"""Live projects early enough that the architect may not be appointed.

WHAT THIS IS FOR
    MVE gets hired by a developer, at feasibility or entitlement, before the
    scheme is drawn. So the only records worth their attention are the ones
    filed BY A DEVELOPER, BEFORE a design team exists. Almost nothing in a
    public construction record qualifies: TDLR names the design firm because
    the design firm filed it, a site plan was drawn by somebody, a design-review
    packet is the architect's own drawing set. Those are records of decisions
    already taken.

    Three sources do qualify, and this pulls them:

    PHOENIX    PUD rezoning narratives carry a project-team block with a slot
               for the architect beside civil, landscape and counsel. Where the
               developer and engineer are named and that slot is EMPTY, a team
               is being assembled without an architect in it. Two cases in
               three do name one, which is what makes the empty third mean
               something.
    CHARLOTTE  Rezoning petitions name the petitioner and no design team at
               all. Absence carries no signal here - it is simply not
               published - but the petitioner is a developer who has just asked
               the city for something.
    RALEIGH    Development plans name the applicant and are current to within
               days. Only about a fifth of filings ever publish drawings, so
               for the rest no architect is on the record.

⚠ SAY THIS WHENEVER THE OUTPUT IS QUOTED
    Not on the record is not the same as not appointed. These are companies
    actively entitling land who have not put an architect in the public file.
    That is a call list. It is not a list of guaranteed open commissions, and
    presenting it as one would be found out on the first phone call.

USAGE
    python live_early_projects.py [months]
"""
import csv
import datetime
import json
import os
import re
import sys
import urllib.parse
import urllib.request

UA = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                    "AppleWebKit/537.36 (KHTML, like Gecko) "
                    "Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0"}
HERE = os.path.dirname(os.path.abspath(__file__))
PIPE = os.path.join(HERE, "..", "docs", "audit-2026-08", "mve-pipeline")

RAL = ("https://services.arcgis.com/v400IkDOw1ad7Yad/ArcGIS/rest/services/"
       "Development_Plans/FeatureServer/0/query")

RESIDENTIAL = re.compile(
    r"(?i)(multi.?family|apartment|townhome|town home|towns\b|residence|"
    r"residential|housing|senior|flats|lofts|villas|condo|dwelling|"
    r"subdivision|homes|cottages|build.?to.?rent|\bBTR\b)")
# Applicants that are plainly consultants rather than the developer.
CONSULTANT = re.compile(
    r"(?i)\b(engineer|engineering|civil|surveying|associates, inc|design, p|"
    r"consultants|civiltek|esp associates|rivers and|clh design|contracting|"
    r"facilities design)\b")


def when(v):
    return (datetime.datetime.fromtimestamp(v / 1000, datetime.timezone.utc)
            .date() if v else None)


def raleigh(months):
    q = urllib.parse.urlencode({
        "where": "submitted >= DATE '2026-01-01'",
        "outFields": "plan_number,plan_name,developer,status,submitted,acreage",
        "returnGeometry": "false", "f": "json",
        "orderByFields": "submitted DESC", "resultRecordCount": 300})
    rows = [f["attributes"] for f in json.load(urllib.request.urlopen(
        urllib.request.Request(RAL + "?" + q, headers=UA), timeout=120)
    ).get("features", [])]
    out = []
    for r in rows:
        name = (r.get("plan_name") or "").replace("DSLC - ", "").strip()
        if not RESIDENTIAL.search(name):
            continue
        if not (r.get("status") or "").lower().startswith(
                ("in review", "submitted", "in appeal")):
            continue
        who = (r.get("developer") or "").strip()
        # The field is the APPLICANT, and on a Raleigh filing that is as often
        # the civil engineer as the developer. Kimley-Horn, ESP Associates and
        # CIVILTEK are not who Dan wants to ring. Mark them rather than drop
        # them - the project is still real, the contact just is not the client.
        out.append({"when": when(r.get("submitted")), "name": name,
                    "who": who, "acres": r.get("acreage"),
                    "is_consultant": bool(CONSULTANT.search(who)) or
                    bool(re.match(r"(?i)kimley|grounded engineering|"
                                  r"crumpler|rdu consulting|gettle", who))})
    return sorted(out, key=lambda r: r["when"] or datetime.date.min,
                  reverse=True)


def charlotte(months):
    p = os.path.join(PIPE, "charlotte-rezoning-pending-2026.json")
    rows = [f["attributes"] for f in json.load(open(p, encoding="utf-8"))["features"]]
    for a in rows:
        a["_d"] = when(a.get("Received"))
    dated = [a for a in rows if a["_d"] and
             (a.get("Status") or "").startswith("Pen")]
    newest = max(a["_d"] for a in dated)
    cut = newest - datetime.timedelta(days=months * 31)
    seen, out = set(), []
    for a in sorted(dated, key=lambda a: a["_d"], reverse=True):
        if a["_d"] < cut or a.get("Petition") in seen:
            continue
        seen.add(a.get("Petition"))
        out.append({"when": a["_d"], "name": str(a.get("Petition")),
                    "who": str(a.get("Petitioner") or "").strip(),
                    "acres": None})
    return out


def phoenix(months):
    p = os.path.join(PIPE, "phoenix-pud-teams-2026.csv")
    out = []
    for r in csv.DictReader(open(p, encoding="utf-8")):
        m = re.match(r"z-\d+-(\d\d)", r.get("case") or "")
        if not m or int(m.group(1)) < 25:
            continue
        dev = (r.get("developer") or "").strip()
        arch = (r.get("architect") or "").strip()
        if re.match(r"(?i)^(s |ure |re |vp lb)", dev) or not dev:
            continue
        if arch and not re.match(r"(?i)kimley|forrest richardson", arch):
            continue          # architect already on the team
        out.append({"when": None, "name": r["case"], "who": dev, "acres": None})
    return out


if __name__ == "__main__":
    months = int(sys.argv[1]) if len(sys.argv) > 1 else 8
    for label, rows in (("PHOENIX  rezoning, architect slot empty", phoenix(months)),
                        ("CHARLOTTE  rezoning petitions pending", charlotte(months)),
                        ("RALEIGH  residential plans in review", raleigh(months))):
        print("=== %s : %d" % (label, len(rows)))
        for r in rows[:16]:
            tag = "  <- consultant, not the client" if r.get("is_consultant") else ""
            print("   %-11s %-40s %-32s%s"
                  % (r["when"] or "", r["name"][:40], r["who"][:32] or "-", tag))
        print()
