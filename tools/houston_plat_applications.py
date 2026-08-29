#!/usr/bin/env python3
"""Houston subdivision plat applications - the market's only pre-design signal.

WHY THIS MATTERS AND THE OTHER HOUSTON SOURCE DOES NOT
    Houston has no zoning, so there is no rezoning petition to watch. The TDLR
    registry is a census of every commercial project over $50k, but a TDLR
    registration is filed BY THE DESIGN FIRM, which means every Houston record
    in it is dated after the architect was appointed. Useful history, useless
    as a lead.

    A subdivision plat is different. It is filed by the developer's surveyor or
    civil engineer to divide the land, it goes to the Houston Planning
    Commission, and it happens BEFORE a building is designed. The public search
    publishes the applicant by name and the date submitted.

    So this is the Houston equivalent of a Clark County pre-application: a
    developer who has committed money to a site and has not yet bought
    architecture.

HOW THE SOURCE WORKS
    plattracker.powerappsportals.us is a Power Apps portal (relaunched
    14 Apr 2025, replacing the old plattracker.houstontx.gov, which is now an
    empty IIS default page). Its public grid reads anonymously, but:

      * data-view-layouts is SINGLE-quoted in the HTML. A double-quote regex
        finds nothing and you conclude the config is absent. It is not.
      * The grid posts Base64SecureConfiguration -- a separate ~23k encrypted
        field INSIDE each layout -- not the 248k layouts blob that contains it.
        Posting the outer blob returns HTTP 500 with an HTML error page.
      * The session cookie matters. Fetch the page and post the grid request on
        the same opener.

    View is "Portal- Public Submitted Applications", sorted pt_name DESC.

⚠ LIMITS
    * A plat is a land action. It says a developer is moving; it does not say
      what will be built, or that no architect is engaged. Treat it as a call,
      not a commission - the same rule as everywhere else in this work.
    * Applicant is often the surveyor or civil engineer rather than the owner.
      That is still a route in, and it is a named firm either way.
    * Plats cover single-family subdivisions too. Filter on scale and type
      before reading anything into a row.

USAGE
    python houston_plat_applications.py [pages] [out.json]
"""
import base64
import http.cookiejar
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

BASE = "https://plattracker.powerappsportals.us"
SEARCH = BASE + "/Application-Search/"
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0")


def opener():
    jar = http.cookiejar.CookieJar()
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))


def bootstrap(op):
    """Return (grid url, secure configuration, sort expression)."""
    html = op.open(urllib.request.Request(SEARCH, headers={"User-Agent": UA}),
                   timeout=120).read().decode("utf-8", "replace")
    pageid = re.search(r"entity-grid-data\.json/([0-9a-f\-]+)", html).group(1)
    blob = re.search(r"data-view-layouts='([^']+)'", html).group(1)
    layouts = json.loads(base64.b64decode(blob).decode("utf-8", "replace"))
    L = layouts[0] if isinstance(layouts, list) else layouts
    return (BASE + "/_services/entity-grid-data.json/" + pageid,
            L["Base64SecureConfiguration"],
            L.get("SortExpression") or "")


def fetch(op, url, secure, sort, page, size=100, search=""):
    body = {
        "base64SecureConfiguration": secure,
        "sortExpression": sort,
        "search": search,
        "page": page,
        "pageSize": size,
        "filter": "",
        "metaFilter": "",
        "timezoneOffset": 0,
        "customParameters": [],
    }
    req = urllib.request.Request(
        url, data=json.dumps(body).encode(), method="POST", headers={
            "User-Agent": UA,
            "Content-Type": "application/json",
            "Accept": "application/json, text/javascript, */*; q=0.01",
            "X-Requested-With": "XMLHttpRequest",
            "Referer": SEARCH,
            "Origin": BASE,
        })
    try:
        r = op.open(req, timeout=180)
        return json.loads(r.read().decode("utf-8", "replace"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")
        t = re.search(r"(?is)<title>(.*?)</title>", body)
        raise SystemExit("grid call failed HTTP %s: %s"
                         % (e.code, t.group(1).strip() if t else body[:160]))


def flatten(rec):
    out = {}
    for a in rec.get("Attributes", []):
        name = a.get("Name")
        val = a.get("DisplayValue")
        if val in (None, ""):
            val = a.get("Value")
        if isinstance(val, dict):
            val = val.get("Name") or val.get("Id")
        if name and val not in (None, ""):
            out[name] = val
    return out


def main(pages=3, out=None):
    op = opener()
    url, secure, sort = bootstrap(op)
    print("view sorted by: %s" % (sort or "(default)"))
    rows, total = [], None
    for p in range(1, pages + 1):
        d = fetch(op, url, secure, sort, p)
        recs = d.get("Records") or []
        total = d.get("ItemCount", total)
        rows.extend(flatten(r) for r in recs)
        print("  page %d: %d rows (of %s total)" % (p, len(recs), total))
        if not recs or len(rows) >= (total or 0):
            break
        time.sleep(0.5)

    print()
    print("collected %d of %s applications" % (len(rows), total))
    if rows:
        print()
        print("  %-14s %-11s %-30s %s"
              % ("APP NO.", "SUBMITTED", "APPLICANT", "SUBDIVISION"))
        for r in rows[:25]:
            print("  %-14s %-11s %-30s %s" % (
                str(r.get("pt_name", ""))[:14],
                str(r.get("pt_appsubmitdate", ""))[:11],
                str(r.get("pt_applicantid", ""))[:30],
                str(r.get("pt_fullsubname_reformatted", ""))[:40]))
    if out:
        with open(out, "w", encoding="utf-8") as fh:
            json.dump({"item_count": total, "rows": rows}, fh, indent=1)
        print()
        print("wrote %s" % out)
    return rows


if __name__ == "__main__":
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 3
    dest = sys.argv[2] if len(sys.argv) > 2 else None
    main(n, dest)
