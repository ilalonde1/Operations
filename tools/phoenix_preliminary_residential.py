#!/usr/bin/env python3
"""Phoenix residential cases still at PRELIMINARY stage — the actionable subset.

⛔ WHY THIS REPLACED A TABLE THAT SHIPPED THREE TIMES
    The Arizona page listed eleven cases, five of them "Final site plan" and
    three of those filed in December 2025. A final site plan is a COMMITTED
    scheme -- the design is done and the architect was engaged a year earlier.
    Printing one as though a reader could act on it is the same error as
    printing a permit. The page even argued for keeping them: "the difference
    tells you how much of the design conversation is still open". That is a
    rationalisation, and the client said so three separate times.

    Only PRELIMINARY belongs in a table a reader is meant to act on. The final
    count still gets STATED, because it answers the literal question asked
    ("everything submitted in Arizona") and because the ratio is genuinely
    interesting -- but it is a number in a sentence, not eleven rows.

⛔ DO NOT USE PROFESS_NAME AS AN ARCHITECT SIGNAL
    The obvious move is to filter on PROFESS_NAME = "TO BE BID" and call the
    result unawarded work. That was tested and it FAILED: "TO BE BID" appears on
    59 of 92 FINAL site plans, so it cannot mean "nobody is hired yet". It is a
    procurement-route field, not a design-team field.

    What is reliable here is the STAGE and the DATE. Nothing in this tool's
    output may be described as "no architect appointed" -- for that, a case has
    to go through tools/find_architect_in_case.py and a trade-press check.

USAGE
    python phoenix_preliminary_residential.py [months] [--all]
"""
import datetime
import json
import re
import sys
import urllib.parse
import urllib.request

LAYER = ("https://maps.phoenix.gov/pub/rest/services/Public/"
         "Planning_Permit/MapServer/0/query")
WHERE = ("(UPPER(PER_TYPE_DESC) LIKE '%SITE PLAN%' OR "
         "UPPER(PER_TYPE_DESC) LIKE '%REZON%') AND UPPER(PERMIT_STAT)='OPEN'")

# Reading residential intent off a project NAME is a judgement, not a field.
# Say so wherever the number is quoted.
RESI = re.compile(r"(?i)apartment|townhome|town home|residence|residential|"
                  r"housing|condo|multifamily|multi-family|senior living|"
                  r"lihtc|villas?\b|lofts?\b|flats\b|homes\b")
NOT_RESI = re.compile(r"(?i)industrial|warehouse|tire|costco|church|clinic|"
                      r"loading dock|infrastructure|garages|remodel|park "
                      r"renovation|office remodel|hotel")


def fetch():
    q = urllib.parse.urlencode({
        "where": WHERE,
        "outFields": ("PERMIT_NAME,PROJECT,PER_TYPE_DESC,PER_ENT_DATE,"
                      "PROFESS_NAME,STREET_FULL_NAME,PER_NUM"),
        "returnGeometry": "false", "f": "json", "resultRecordCount": 2000})
    with urllib.request.urlopen(LAYER + "?" + q, timeout=180) as r:
        return [f["attributes"] for f in json.load(r).get("features", [])]


def main(months=8, show_all=False):
    rows = fetch()
    for r in rows:
        ms = r.get("PER_ENT_DATE")
        r["_d"] = (datetime.datetime.fromtimestamp(
            ms / 1000, datetime.timezone.utc).date() if ms else None)
    cutoff = datetime.date.today() - datetime.timedelta(days=months * 31)
    recent = [r for r in rows if r["_d"] and r["_d"] >= cutoff]

    def stage(r):
        t = (r.get("PER_TYPE_DESC") or "").upper()
        return "preliminary" if "PRELIMINARY" in t else "final"

    prelim = [r for r in recent if stage(r) == "preliminary"]
    final = [r for r in recent if stage(r) == "final"]

    def name(r):
        return (r.get("PERMIT_NAME") or r.get("PROJECT") or "?").strip()

    resi = [r for r in prelim
            if RESI.search(name(r)) and not NOT_RESI.search(name(r))]
    # a bare number for a name tells the reader nothing
    resi = [r for r in resi if not re.match(r"^[\d\-]+$", name(r))]
    seen, uniq = set(), []
    for r in sorted(resi, key=lambda r: r["_d"], reverse=True):
        k = (name(r).upper(), r["_d"])
        if k in seen:
            continue
        seen.add(k)
        uniq.append(r)

    print("open site-plan / rezoning cases        : %d" % len(rows))
    print("filed in the last %d months             : %d" % (months, len(recent)))
    print("   PRELIMINARY (still being shaped)    : %d" % len(prelim))
    print("   FINAL (committed - NOT actionable)  : %d" % len(final))
    print("   preliminary AND residential by name : %d" % len(uniq))
    print()
    print("⚠ residential is read off the project NAME, which is a judgement,")
    print("  not a field. Quote it as an indication.")
    print("⛔ none of this says an architect is unappointed. Stage only.")
    print()
    print("%-11s %-9s %-40s %s" % ("FILED", "CASE", "PROJECT", "ADDRESS"))
    for r in (uniq if show_all else uniq[:14]):
        print("%-11s %-9s %-40s %s"
              % (r["_d"].isoformat(), str(r.get("PER_NUM", ""))[:9],
                 name(r)[:40], (r.get("STREET_FULL_NAME") or "")[:34]))
    return uniq


if __name__ == "__main__":
    a = [x for x in sys.argv[1:] if not x.startswith("--")]
    main(int(a[0]) if a else 8, "--all" in sys.argv)
