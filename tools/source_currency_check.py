#!/usr/bin/env python3
"""How current is each market's record, TODAY. Ask the source, do not assume.

WHY THIS EXISTS
    A dossier that says "current to the day this was run" has to mean it. The
    Phoenix Plan Review layer is live and its newest row is from yesterday --
    but its newest SITE PLAN or REZONING case is three months old, because the
    freshness of a service says nothing about the freshness of the slice you
    query. A reader who looks for last month and finds May discounts everything
    else in the document.

    So this asks every source for its newest record, per slice, and prints the
    answer. Run it before any send.

USAGE
    python source_currency_check.py
"""
import datetime
import json
import urllib.parse
import urllib.request

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0")


def arcgis_newest(base, layer, datefield, where="1=1"):
    """Newest value of datefield, by sorting descending -- outStatistics max
    is refused by some of these services."""
    q = urllib.parse.urlencode({
        "where": where, "outFields": datefield,
        "orderByFields": datefield + " DESC",
        "returnGeometry": "false", "f": "json", "resultRecordCount": 1})
    url = "%s/%s/query?%s" % (base.rstrip("/"), layer, q)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=90) as r:
        d = json.load(r)
    fs = d.get("features") or []
    if not fs:
        return None
    v = fs[0]["attributes"].get(datefield)
    if not v:
        return None
    return datetime.datetime.fromtimestamp(
        v / 1000, datetime.timezone.utc).date()


PHX = "https://maps.phoenix.gov/pub/rest/services/Public/Planning_Permit/MapServer"
MIA = ("https://services1.arcgis.com/CvuPhqcTQpZPT9qY/ArcGIS/rest/services/"
       "Building_Permits_Since_2014/FeatureServer")
RAL = ("https://services.arcgis.com/v400IkDOw1ad7Yad/ArcGIS/rest/services/"
       "Development_Plans/FeatureServer")
SITE_REZONE = ("(UPPER(PER_TYPE_DESC) LIKE '%SITE PLAN%' OR "
               "UPPER(PER_TYPE_DESC) LIKE '%REZON%')")

CHECKS = [
    ("Phoenix", "building permits", PHX, 1, "PER_ENT_DATE", "1=1"),
    ("Phoenix", "all plan reviews", PHX, 0, "PER_ENT_DATE", "1=1"),
    ("Phoenix", "site plan / rezoning ONLY", PHX, 0, "PER_ENT_DATE", SITE_REZONE),
    ("Miami", "building permits issued", MIA, 0, "IssuedDate", "1=1"),
    ("Miami", "permits first submitted", MIA, 0, "FirstSubmissionDate", "1=1"),
    ("Raleigh", "development plans updated", RAL, 0, "updated", "1=1"),
    ("Raleigh", "development plans submitted", RAL, 0, "submitted", "1=1"),
]

# Not ArcGIS. Clark County, Charlotte and Dallas are Accela Citizen Access,
# which serves HTML and has no date API -- their currency is established with
# tools/aca_permit_probe.py, and Houston's with tools/tabs_projects.py.
NOT_QUERYABLE_HERE = [
    ("Houston", "TDLR registrations", "tools/tabs_projects.py list"),
    ("Las Vegas", "Clark County, Accela ACA", "tools/aca_permit_probe.py"),
    ("Charlotte", "Accela ACA", "tools/aca_permit_probe.py"),
]

if __name__ == "__main__":
    today = datetime.date.today()
    print("today: %s\n" % today.isoformat())
    print("%-10s %-30s %-12s %s" % ("MARKET", "SLICE", "NEWEST", "LAG"))
    for market, slice_, base, layer, field, where in CHECKS:
        try:
            d = arcgis_newest(base, layer, field, where)
            lag = "%d days" % (today - d).days if d else "-"
            print("%-10s %-30s %-12s %s" % (market, slice_, d or "none", lag))
        except Exception as e:
            print("%-10s %-30s %s" % (market, slice_, str(e)[:44]))
    print()
    for market, slice_, how in NOT_QUERYABLE_HERE:
        print("%-10s %-30s %s" % (market, slice_, "-> " + how))
