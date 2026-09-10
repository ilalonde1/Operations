"""Hunt development-application feeds for every municipality at once.

Vancouver was found by hand, after being declared absent. Doing that 45 more
times is how the next one gets missed, so this applies the hunting order from
reference_island_permit_feed_hunting to the whole list in one run:

  1. ArcGIS Online item search  - finds hosted feature services wherever they
     live, which is what caught the municipalities whose own /rest/services
     root is closed or on an unguessable host.
  2. EngagementHQ /projects.json - the platform Vancouver and the RDN turned out
     to use, and the one nobody thinks to check because it looks like a
     consultation site rather than a permit feed.

It reports CANDIDATES, not conclusions. Every hit still has to be opened and
looked at before anything is wired - a "Development Permit Area" polygon layer
returns perfectly valid features and would ingest cleanly as nonsense.
"""
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/128.0 Safari/537.36"

TARGETS = [
    ("Surrey", "surrey.ca", ["Surrey"]),
    ("Burnaby", "burnaby.ca", ["Burnaby"]),
    ("Richmond", "richmond.ca", ["Richmond BC"]),
    ("Abbotsford", "abbotsford.ca", ["Abbotsford"]),
    ("Kelowna", "kelowna.ca", ["Kelowna"]),
    ("Langley Township", "tol.ca", ["Township of Langley"]),
    ("Delta", "delta.ca", ["Delta BC"]),
    ("Kamloops", "kamloops.ca", ["Kamloops"]),
    ("Chilliwack", "chilliwack.com", ["Chilliwack"]),
    ("North Vancouver District", "dnv.org", ["District of North Vancouver"]),
    ("New Westminster", "newwestcity.ca", ["New Westminster"]),
    ("Port Coquitlam", "portcoquitlam.ca", ["Port Coquitlam"]),
    ("North Vancouver City", "cnv.org", ["City of North Vancouver"]),
    ("West Vancouver", "westvancouver.ca", ["West Vancouver"]),
    ("Nanaimo", "nanaimo.ca", ["Nanaimo"]),
    ("Vernon", "vernon.ca", ["Vernon BC"]),
    ("Penticton", "penticton.ca", ["Penticton"]),
    ("Port Moody", "portmoody.ca", ["Port Moody"]),
    ("Mission", "mission.ca", ["Mission BC"]),
    ("Langley City", "langleycity.ca", ["City of Langley"]),
    ("White Rock", "whiterockcity.ca", ["White Rock"]),
    ("Pitt Meadows", "pittmeadows.ca", ["Pitt Meadows"]),
    ("North Cowichan", "northcowichan.ca", ["North Cowichan"]),
    ("West Kelowna", "westkelownacity.ca", ["West Kelowna"]),
    ("Sooke", "sooke.ca", ["Sooke"]),
    ("Esquimalt", "esquimalt.ca", ["Esquimalt"]),
    ("Oak Bay", "oakbay.ca", ["Oak Bay"]),
    ("Central Saanich", "csaanich.ca", ["Central Saanich"]),
    ("Sidney", "sidney.ca", ["Sidney BC"]),
    ("North Saanich", "northsaanich.ca", ["North Saanich"]),
    ("Port Alberni", "portalberni.ca", ["Port Alberni"]),
    ("Duncan", "duncan.ca", ["Duncan BC"]),
    ("Powell River", "powellriver.ca", ["Powell River"]),
    ("Salmon Arm", "salmonarm.ca", ["Salmon Arm"]),
    ("Lake Country", "lakecountry.bc.ca", ["Lake Country"]),
    ("Summerland", "summerland.ca", ["Summerland"]),
    ("Bowen Island", "bowenislandmunicipality.ca", ["Bowen Island"]),
    ("Hope", "hope.ca", ["District of Hope"]),
    ("Kent", "district.kent.bc.ca", ["District of Kent"]),
]

# EngagementHQ tenants are named to sound like consultation, never like permits.
EHQ_PREFIXES = ["letstalk", "engage", "shapeyourcity", "yoursay", "connect", "talk",
                "haveyoursay", "getinvolved", "shape"]


def get(url, timeout=25):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read()


def arcgis_search(term):
    q = ('(title:"development application" OR title:"development permit" OR '
         'title:"development tracker" OR title:"rezoning" OR title:"active applications") '
         'AND "%s"' % term)
    url = ("https://www.arcgis.com/sharing/rest/search?f=json&num=20&sortField=numviews"
           "&sortOrder=desc&q=" + urllib.parse.quote(q))
    try:
        d = json.loads(get(url))
    except Exception as e:
        return [("ERROR", str(e)[:60])]
    out = []
    for it in d.get("results", []):
        if it.get("type") in ("Feature Service", "Map Service", "Feature Layer"):
            out.append((it.get("title", "")[:56], it.get("url") or it.get("id")))
    return out


def ehq(domain):
    base = domain.split(".")[0]
    hits = []
    for p in EHQ_PREFIXES:
        for host in ("%s%s.ca" % (p, base), "%s.%s" % (p, domain)):
            try:
                body = get("https://%s/projects.json" % host, timeout=12)
            except Exception:
                continue
            try:
                d = json.loads(body)
            except Exception:
                continue
            if isinstance(d, dict) and "projects" in d:
                n = len(d["projects"])
                rx = re.compile(r"development\s+application|rezoning|zoning\s+amendment", re.I)
                apps = [p2 for p2 in d["projects"] if rx.search(p2.get("name") or "")]
                live = [p2 for p2 in apps if not p2.get("archived")]
                hits.append((host, n, len(apps), len(live)))
    return hits


only = sys.argv[1:] or None
print("%-26s %s" % ("MUNICIPALITY", "CANDIDATES"))
print("-" * 100)
for name, domain, terms in TARGETS:
    if only and name not in only:
        continue
    found = []
    for t in terms:
        for title, url in arcgis_search(t):
            found.append("ArcGIS  %-52s %s" % (title, (url or "")[:70]))
    for host, n, apps, live in ehq(domain):
        found.append("EHQ     %-52s %d projects, %d applications, %d live"
                     % (host, n, apps, live))
    print("%-26s %s" % (name, ("%d candidate(s)" % len(found)) if found else "- nothing found"))
    for f in found:
        print("      %s" % f)
    sys.stdout.flush()
    time.sleep(0.3)
