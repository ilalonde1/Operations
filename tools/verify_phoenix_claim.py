"""Re-derive the three Phoenix figures that open Ian's email."""
import json
import urllib.parse
import urllib.request
from collections import Counter

LAYER = ("https://maps.phoenix.gov/pub/rest/services/Public/"
         "Planning_Permit/MapServer/0/query")
SITE_REZONE = ("(UPPER(PER_TYPE_DESC) LIKE '%SITE PLAN%' OR "
               "UPPER(PER_TYPE_DESC) LIKE '%REZON%')")
OPEN = "UPPER(PERMIT_STAT) = 'OPEN'"


def count(where):
    q = urllib.parse.urlencode({"where": where, "returnCountOnly": "true",
                                "f": "json"})
    with urllib.request.urlopen(LAYER + "?" + q, timeout=90) as r:
        return json.load(r).get("count")


def rows(where, fields, n=1500):
    q = urllib.parse.urlencode({"where": where, "outFields": fields,
                                "returnGeometry": "false", "f": "json",
                                "resultRecordCount": n})
    with urllib.request.urlopen(LAYER + "?" + q, timeout=120) as r:
        return [f["attributes"] for f in json.load(r).get("features", [])]


print("PRINTED IN THE DOSSIER AND THE EMAIL -> RE-DERIVED TODAY")
print("  13,022 plan reviews            ->", count("1=1"))
open_cases = count("%s AND %s" % (SITE_REZONE, OPEN))
print("  373 open site-plan/rezoning    ->", open_cases)

since = ("%s AND %s AND PER_ENT_DATE >= DATE '2025-01-01'"
         % (SITE_REZONE, OPEN))
print("  280 of those filed since 1 Jan 2025 ->", count(since))

recs = rows(since, "PROJECT,PERMIT_NAME,PER_TYPE_DESC,PER_ENT_DATE")
KEYS = ("APARTMENT", "MULTIFAMILY", "MULTI-FAMILY", "RESIDENC", "RESIDENTIAL",
        "CONDO", "TOWNHOME", "TOWNHOUSE", "SENIOR", "HOUSING", "VILLAS",
        "LOFTS", "FLATS", "DWELLING", "HOMES", "LIVING")
res = [r for r in recs
       if any(k in ((r.get("PROJECT") or "") + " " +
                    (r.get("PERMIT_NAME") or "")).upper() for k in KEYS)]
print("  41 of those residential by name ->", len(res),
      "   (of %d rows returned)" % len(recs))

print("\ncase types inside the filter:")
for k, v in Counter((r.get("PER_TYPE_DESC") or "?") for r in recs).most_common(8):
    print("   %-48s %d" % (k[:48], v))
print("\nfive of the residential ones, as a spot check:")
for r in res[:5]:
    print("   %-44s %s" % ((r.get("PROJECT") or r.get("PERMIT_NAME") or "?")[:44],
                           (r.get("PER_TYPE_DESC") or "")[:30]))
