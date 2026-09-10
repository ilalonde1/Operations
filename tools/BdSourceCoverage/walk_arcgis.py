"""Walk an ArcGIS server root, folders and all, and report layers that look like
development APPLICATIONS rather than zoning overlays.

The distinction is the whole game. "Development Permit Area" is a polygon
overlay saying where rules apply; it returns valid features and would ingest
cleanly as nonsense. An applications layer has a file number and a status.
"""
import json
import sys
import urllib.parse
import urllib.request

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/128.0 Safari/537.36"

WANT = ("develop", "rezon", "applic", "permit", "subdiv", "planning", "project")
# These are overlays, not applications - the false positive to filter out.
OVERLAY = ("permit area", "dpa", "hazard", "wildfire", "streamside", "riparian",
           "farming", "sensitive", "aquatic", "form and character", "catchment")


def get(url, timeout=25):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read())


def walk(root):
    print("== %s" % root)
    try:
        d = get(root + "?f=json")
    except Exception as e:
        print("   unreachable: %s" % str(e)[:70])
        return
    folders = d.get("folders", [])
    services = list(d.get("services", []))
    for f in folders:
        try:
            services += get("%s/%s?f=json" % (root, f)).get("services", [])
        except Exception:
            pass
    print("   %d folder(s), %d service(s)" % (len(folders), len(services)))

    cands = []
    for s in services:
        n = s["name"].lower()
        if any(w in n for w in WANT) and not any(o in n for o in OVERLAY):
            cands.append(s)
    print("   %d service(s) look like applications" % len(cands))

    for s in cands:
        url = "%s/%s/%s" % (root, s["name"].split("/")[-1], s["type"]) \
            if "/" not in s["name"] else "%s/%s/%s" % (root, s["name"], s["type"])
        try:
            meta = get(url + "?f=json")
        except Exception as e:
            print("      %-46s (meta failed %s)" % (s["name"], str(e)[:30]))
            continue
        layers = meta.get("layers", []) or []
        for l in layers:
            ln = (l.get("name") or "").lower()
            if any(w in ln for w in WANT) and not any(o in ln for o in OVERLAY):
                lurl = "%s/%s" % (url, l["id"])
                try:
                    cnt = get(lurl + "/query?where=1%3D1&returnCountOnly=true&f=json")
                    n = cnt.get("count", "?")
                except Exception:
                    n = "?"
                print("      %-58s %s rows" % (l.get("name"), n))
                print("         %s" % lurl)


for root in sys.argv[1:]:
    walk(root.rstrip("/"))
    print()
