#!/usr/bin/env python3
"""Phoenix cases filed recently where the professional is NOT yet appointed.

WHY THIS EXISTS, AND WHY THE HOUSTON LIST IS NOT THIS
    A Texas TDLR registration names the DESIGN FIRM, because the design firm is
    the one filing it. By the time a project appears there the seat is gone. It
    is an excellent record of WHO IS BUILDING AND WHO THEY USED, and it is
    useless as a heads-up.

    Phoenix is the opposite. Its planning record carries a PROFESS_NAME field
    that, on a large share of open site-plan cases, literally reads "TO BE BID"
    -- the city's own words for work that has not been awarded. Filter that to
    cases filed in the last few months and at PRELIMINARY stage, and the result
    is a list of schemes where somebody still has to be hired.

    Preliminary means the scheme is still being shaped. Final means it is
    committed. The difference is the whole value of the list.

USAGE
    python phoenix_open_seats.py [months_back]
"""
import datetime
import json
import sys
import urllib.parse
import urllib.request

LAYER = ("https://maps.phoenix.gov/pub/rest/services/Public/"
         "Planning_Permit/MapServer/0/query")
WHERE = ("(UPPER(PER_TYPE_DESC) LIKE '%SITE PLAN%' OR "
         "UPPER(PER_TYPE_DESC) LIKE '%REZON%') AND UPPER(PERMIT_STAT)='OPEN'")
UNAWARDED = {"TO BE BID", "", "OWNER", "TBD", "N/A"}


def fetch():
    q = urllib.parse.urlencode({
        "where": WHERE,
        "outFields": "PERMIT_NAME,PROJECT,PER_TYPE_DESC,PER_ENT_DATE,"
                     "PROFESS_NAME,STREET_FULL_NAME",
        "returnGeometry": "false", "f": "json", "resultRecordCount": 2000})
    with urllib.request.urlopen(LAYER + "?" + q, timeout=120) as r:
        return [f["attributes"] for f in json.load(r).get("features", [])]


def main(months=6):
    rows = fetch()
    cutoff = datetime.date.today() - datetime.timedelta(days=months * 31)
    for r in rows:
        ms = r.get("PER_ENT_DATE")
        r["_d"] = (datetime.datetime.fromtimestamp(
            ms / 1000, datetime.timezone.utc).date() if ms else None)

    recent = [r for r in rows if r["_d"] and r["_d"] >= cutoff]
    open_seat = [r for r in recent
                 if (r.get("PROFESS_NAME") or "").strip().upper() in UNAWARDED]
    prelim = [r for r in open_seat
              if "PRELIMINARY" in (r.get("PER_TYPE_DESC") or "").upper()]

    print("open site-plan / rezoning cases          : %d" % len(rows))
    print("filed in the last %d months               : %d" % (months, len(recent)))
    print("of those, no professional appointed yet  : %d" % len(open_seat))
    print("   and still at PRELIMINARY stage        : %d" % len(prelim))
    print()
    print("%-46s %-11s %s" % ("PROJECT", "FILED", "STAGE"))
    for r in sorted(prelim, key=lambda r: r["_d"], reverse=True):
        stage = ("preliminary" if "PRELIMINARY" in r["PER_TYPE_DESC"].upper()
                 else "final")
        name = (r.get("PERMIT_NAME") or r.get("PROJECT") or "?").strip()
        print("%-46s %-11s %s" % (name[:46], r["_d"].isoformat(), stage))


if __name__ == "__main__":
    main(int(sys.argv[1]) if len(sys.argv) > 1 else 6)
